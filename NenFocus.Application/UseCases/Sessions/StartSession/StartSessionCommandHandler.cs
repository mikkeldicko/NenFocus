using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionCommandHandler
    : ICommandHandler<StartSessionCommand, StartSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockRepository _blocks;
    private readonly IBreakTimerPresetRepository _breakTimerPresets;
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;
    private readonly BlockPlacementChecker _placement;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IBlockRepository blocks,
        IBreakTimerPresetRepository breakTimerPresets,
        INenContractRepository contracts,
        ISystemClock clock,
        BlockPlacementChecker placement)
    {
        _sessions = sessions;
        _blocks = blocks;
        _breakTimerPresets = breakTimerPresets;
        _contracts = contracts;
        _clock = clock;
        _placement = placement;
    }

    public async Task<StartSessionResult> HandleAsync(
        StartSessionCommand command,
        CancellationToken ct)
    {
        var now = _clock.Now;

        // Every session belongs to a block (Gap 114, D2): a planned session starts against the
        // block its Ritual was triggered for; a spontaneous one books a walk-in block for its slot.
        FocusBlock block;
        if (command.OriginatingBlockId is { } originatingBlockId)
        {
            var planned = await _blocks.GetByIdAsync(originatingBlockId, ct) as FocusBlock;
            if (planned is null)
                return StartSessionResult.Fail("The originating focus block was not found.");

            if (planned.HasEnded(now))
                return StartSessionResult.Fail(
                    "This focus block's window has already closed. It will be recorded as missed instead.");

            if (await _sessions.GetByOriginatingBlockIdAsync(originatingBlockId, ct) is not null)
                return StartSessionResult.Fail("A session already exists for this focus block.");

            block = planned;
        }
        else
        {
            var slot = new TimeSlot(now, now + TimeSpan.FromMinutes(command.DurationMinutes));
            var calendar = await _blocks.GetForRangeAsync(slot, ct);
            var running = await _sessions.GetActiveAsync(ct);
            var contractsInEffect = await _contracts.GetAllInEffectAsync(ct);

            // The full placement question (Gap 114): a walk-in block goes only where Focus time is
            // free and its slot fits any governing contract — the same rule the Planner asks, worded
            // in the same place.
            var verdict = _placement.Check(
                slot, BlockPresetType.Focus, membership: null,
                contractsInEffect, calendar, running, now, _clock.TimeZone);

            if (verdict.Outcome != PlacementOutcome.Allowed)
                return StartSessionResult.Fail(
                    BlockPlacementVerdictDto.From(verdict).Message ?? "This focus time isn't available.");

            // A walk-in inside a contract is Bonus (Gap 114, D8): it belongs to the contract but
            // never counts toward its floor. The contract makes its own membership.
            var membership = verdict.GoverningContractId is { } governingContractId
                ? (await _contracts.GetByIdAsync(governingContractId, ct))?.MembershipForPlacedBlock()
                : null;

            block = FocusBlock.Spontaneous(Guid.NewGuid(), command.PresetId!.Value, slot, membership: membership);
            _blocks.Add(block);
        }

        // Resolved once, here, and copied onto the session (Gap 64, D8): a block that belongs to a
        // contract — planned, or a Bonus walk-in (Gap 97) — inherits that contract's rule; a plain
        // block keeps the default of lifting blocking on breaks.
        var keepBlockingDuringBreaks = block.Membership?.ContractId is { } contractId
            && (await _contracts.GetByIdAsync(contractId, ct))?.KeepBlockingDuringBreaks == true;

        // The preset is only a quick way to produce the schedule — the session stores the
        // resulting breaks, never the preset or its rhythm (Gap 63). Laid over the block's slot.
        var breakSchedule = BreakSchedule.None;
        string? breakPresetName = null;
        if (command.BreakTimerPresetId is { } breakTimerPresetId)
        {
            var breakTimerPreset = await _breakTimerPresets.GetByIdAsync(breakTimerPresetId, ct);
            if (breakTimerPreset is null)
                return StartSessionResult.Fail("The selected break timer preset was not found.");

            breakSchedule = breakTimerPreset.BreakTimer.ScheduleFor(block.Scheduled.Length);
            breakPresetName = breakTimerPreset.Name;
        }

        // The one creation path: the session is the run, its block the booking (Gap 114, D6), and
        // the block's arrival is stamped inside Start. The app's view of the running session
        // refreshes itself from the database once this commits (SessionStartedEventHandler) —
        // nothing is pushed from here (Gap 92, A3).
        var session = Session.Start(
            id: Guid.NewGuid(),
            goal: new SessionGoal(command.Goal),
            block: block,
            startedAt: now,
            temporaryBlockedApps: command.AdditionalBlockedApps.Select(a => new BlockedApp(a)).Distinct().ToList(),
            temporaryBlockedSites: command.AdditionalBlockedSites.Select(s => new Website(s)).Distinct().ToList(),
            breakSchedule: breakSchedule,
            breakPresetName: breakPresetName,
            keepBlockingDuringBreaks: keepBlockingDuringBreaks);

        _sessions.Add(session);

        return StartSessionResult.Ok(session.Id);
    }
}
