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
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly ISessionStateService _sessionState;
    private readonly IBlockPresetRepository _presets;
    private readonly IBreakTimerPresetRepository _breakTimerPresets;
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;
    private readonly IBlockPlacementChecker _placement;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISessionStateService sessionState,
        IBlockPresetRepository presets,
        IBreakTimerPresetRepository breakTimerPresets,
        INenContractRepository contracts,
        ISystemClock clock,
        IBlockPlacementChecker placement)
    {
        _sessions = sessions;
        _plannedBlocks = plannedBlocks;
        _sessionState = sessionState;
        _presets = presets;
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

        // Is the requested run free of other Focus blocks? BlockPlacementChecker.CheckFree (Gap 88),
        // leaving out the block this session is for. Running sessions aren't passed yet: only one
        // runs at a time today, and phase 8 words that case properly.
        var requested = new TimeSlot(now, now + TimeSpan.FromMinutes(command.DurationMinutes));
        var calendar = (await _plannedBlocks.GetForRangeAsync(requested, ct)).Where(b => b.Id != command.OriginatingBlockId);
        var verdict = _placement.CheckFree(requested, calendar, [], now);

        if (verdict.Conflict is { } conflict)
        {
            var minutesAvailable = (int)conflict.StartsIn(now).TotalMinutes;
            if (minutesAvailable > 0)
                return StartSessionResult.Fail(
                    $"A planned focus block starts in {minutesAvailable} minutes. " +
                    $"Please set your duration to {minutesAvailable} minutes or less.");

            return StartSessionResult.Fail(
                "A focus block is currently active. Please complete the Ritual instead of starting a spontaneous session.");
        }

        FocusBlock? originatingBlock = null;
        if (command.OriginatingBlockId.HasValue)
        {
            originatingBlock = await _plannedBlocks.GetByIdAsync(command.OriginatingBlockId.Value, ct) as FocusBlock;

            if (originatingBlock is null)
                return StartSessionResult.Fail("The originating focus block was not found.");

            if (originatingBlock.HasEnded(now))
                return StartSessionResult.Fail(
                    "This focus block's window has already closed. It will be recorded as missed instead.");

            if (await _sessions.ExistsForOriginatingBlockAsync(command.OriginatingBlockId.Value, ct))
                return StartSessionResult.Fail("A session already exists for this focus block.");
        }

        var origin = command.OriginatingBlockId.HasValue
            ? SessionOrigin.Planned
            : SessionOrigin.Spontaneous;

        // For a Planned session, Duration ("Planned") is the block's full nominal
        // length — a fixed fact about the schedule, independent of exactly when the
        // Ritual completes — not "time remaining from Ritual completion." This is the
        // invariant that makes FocusBlock.CreditedSlot (EndSessionCommandHandler) always
        // produce a "Duration" ≤ "Planned": a graced-within-5-minutes credit maxes out at
        // exactly this same (End - Start) figure, and actual elapsed time can never
        // exceed it either, since StartedAt ≥ Start and EndedAt ≤ End by construction. A
        // planned block can't be credited for longer than it was ever planned to run.
        var duration = originatingBlock is not null
            ? originatingBlock.Scheduled.Length
            : TimeSpan.FromMinutes(command.DurationMinutes);

        var session = Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal(command.Goal),
            duration: new SessionDuration(duration),
            blockPresetId: command.PresetId ?? Guid.Empty,
            origin: origin,
            originatingBlockId: command.OriginatingBlockId,
            temporaryBlockedApps: command.AdditionalBlockedApps.Count > 0 ? command.AdditionalBlockedApps : null,
            temporaryBlockedSites: command.AdditionalBlockedSites.Count > 0 ? command.AdditionalBlockedSites : null);

        // The preset is only a quick way to produce the schedule — the session stores the
        // resulting breaks, never the preset or its rhythm (Gap 63). Laid over the run the
        // session will actually have (a late planned start has less of it), so a preset that
        // no longer has room for a single break simply yields none.
        var breakSchedule = BreakSchedule.None;
        string? breakPresetName = null;
        if (command.BreakTimerPresetId is { } breakTimerPresetId)
        {
            var breakTimerPreset = await _breakTimerPresets.GetByIdAsync(breakTimerPresetId, ct);
            if (breakTimerPreset is null)
                return StartSessionResult.Fail("The selected break timer preset was not found.");

            breakSchedule = breakTimerPreset.BreakTimer.ScheduleFor(session.RunLength(now, originatingBlock?.Scheduled));
            breakPresetName = breakTimerPreset.Name;
        }

        // Resolved once, here, and copied onto the session (Gap 64): a planned block that
        // belongs to a contract — Bonus blocks included (Gap 97) — inherits that contract's
        // rule; a Spontaneous session or a plain block keeps the default of lifting blocking
        // on breaks. The one place a future global Settings default would plug in.
        var keepBlockingDuringBreaks = originatingBlock?.Membership?.ContractId is { } contractId
            && (await _contracts.GetByIdAsync(contractId, ct))?.KeepBlockingDuringBreaks == true;

        session.Start(now, originatingBlock?.Scheduled, breakSchedule, breakPresetName, keepBlockingDuringBreaks);

        if (originatingBlock is not null)
        {
            originatingBlock.MarkRitualCompleted(now);
            await _plannedBlocks.SaveAsync(originatingBlock, ct);
        }

        await _sessions.SaveAsync(session, ct);

        var presetName = string.Empty;
        if (command.PresetId is { } pid && pid != Guid.Empty)
        {
            var preset = await _presets.GetByIdAsync(pid, ct);
            presetName = preset?.Name ?? string.Empty;
        }

        _sessionState.SetActive(new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            PresetName = presetName,
            BlockPresetId = session.BlockPresetId,
            DurationMinutes = (int)duration.TotalMinutes,
            StartedAt = session.StartedAt ?? now,
            EndAt = session.EndAt ?? now,
            Status = session.Status,
            Origin = session.Origin,
            OriginatingBlockId = session.OriginatingBlockId,
            Breaks = SessionDto.MapBreaks(session.BreakSchedule),
            BreakPresetName = session.BreakPresetName,
            KeepBlockingDuringBreaks = session.KeepBlockingDuringBreaks
        });

        return StartSessionResult.Ok(session.Id);
    }
}
