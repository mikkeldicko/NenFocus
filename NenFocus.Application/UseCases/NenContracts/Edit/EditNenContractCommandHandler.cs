using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.NenContracts.Edit;

/// <summary>
/// Deliberately not composed through ICommandHandler&lt;SealNenContractCommand,...&gt; — every
/// command handler is wrapped by TransactionalCommandHandler, which commits once after it
/// returns; nesting a second command call here would trigger a second, separate commit,
/// reopening the non-atomic risk this slice exists to avoid. Instead this handler reuses the
/// same domain/query pieces SealNenContractCommandHandler itself uses (own orchestration
/// shell, matching MODULE_STANDARDS.md's "no shared command scaffolding, copy the pattern"
/// rule) so both stay in the single commit the outer TransactionalCommandHandler performs.
/// Empirically confirmed (against a real SQLite-backed DbContext) that removing a tracked
/// entity and adding a different instance under the same key resolves correctly in one
/// SaveChangesAsync — no separate transaction juggling needed.
/// </summary>
public sealed class EditNenContractCommandHandler : ICommandHandler<EditNenContractCommand, EditNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IContractBlockBatchPreparer _batchPreparer;
    private readonly ISessionRepository _sessions;
    private readonly IBlockPlacementChecker _placement;
    private readonly ISystemClock _clock;

    public EditNenContractCommandHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IContractBlockBatchPreparer batchPreparer,
        ISessionRepository sessions,
        IBlockPlacementChecker placement,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _batchPreparer = batchPreparer;
        _sessions = sessions;
        _placement = placement;
        _clock = clock;
    }

    public async Task<EditNenContractResult> HandleAsync(EditNenContractCommand command, CancellationToken ct = default)
    {
        var oldContract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (oldContract is null)
            return EditNenContractResult.Fail("Contract not found.");

        if (oldContract.CheckEdit() is { IsAllowed: false } editVerdict)
            return EditNenContractResult.Fail(ContractActionVerdictDto.From(editVerdict).Message!);

        // Reuses the SAME id — literal identity preservation, not a new contract.
        var contractId = command.ContractId;

        var preparation = _batchPreparer.Prepare(
            command.Blocks.Select(spec => (spec.PresetId, spec.Slot, spec.IsFocus, spec.Title)).ToList(),
            contractId,
            _clock.TimeZone);

        if (preparation.Outcome == ContractBlockBatchOutcome.OverlapsWithinBatch)
            return EditNenContractResult.Fail("Two Focus blocks in this schedule overlap each other.");

        var now = _clock.Now;

        // The settings' own answer, asked again here so a caller that skipped the validator gets the
        // sentence, not ContractRules' exception (Gap 92, V9).
        if (ContractSettingsVerdictDto.Check(command.FixedDailyFloorHours, command.WorkHours).Message is { } settingsMessage)
            return EditNenContractResult.Fail(settingsMessage);

        var rules = new ContractRules(
            command.StrictnessRule,
            command.FixedDailyFloorHours is { } fixedHours ? new DailyFloor(fixedHours) : null,
            command.AllowCancellation,
            command.KeepBlockingDuringBreaks,
            command.RedemptionPlacementPolicy,
            command.BonusBlocksFollowStrictness);

        // The rules say how days get their floor (ContractRules.FloorRule). With no per-day
        // obligation, whatever was drafted is the whole-period target (NenContract.HasPeriodTarget).
        var floorRule = rules.FloorRule;

        var workHours = command.WorkHours;

        var localNow = _clock.TimeZone.ToCalendarTime(now);

        NenContract newContract;
        try
        {
            // Same bridge as SealNenContractCommandHandler until Open Gap W adds a start time.
            var calendar = ContractCalendar.Generate(
                localStart: ContractCalendar.SuggestedStart(command.StartDate, workHours, command.NoWorkHoursDays, localNow),
                endDate: command.EndDate,
                endTime: null,
                workHours: workHours,
                noWorkHoursDays: command.NoWorkHoursDays,
                floorRule: floorRule,
                restDays: command.RestDays,
                plannedFocusHoursByDay: preparation.PlannedFocusHoursByDay);

            newContract = NenContract.Seal(
                id: contractId,
                name: command.Name,
                goal: new NenContractGoal(command.Goal),
                rules: rules,
                calendar: calendar,
                plannedFocusHoursByDay: preparation.PlannedFocusHoursByDay,
                sealedAt: now,
                timeZone: _clock.TimeZone,
                contractPresetId: command.ContractPresetId);
        }
        catch (ArgumentException ex)
        {
            return EditNenContractResult.Fail(ex.Message);
        }

        // Same check as SealNenContractCommandHandler — every block asks BlockPlacementChecker.Check
        // (Gap 88 phase 8), against the calendar as it will be: without this contract's own old
        // blocks, which its new ones replace.
        var running = await _sessions.GetActiveAsync(ct);
        foreach (var block in preparation.Blocks)
        {
            var calendar = (await _blocks.GetForRangeAsync(block.Scheduled, ct)).Where(b => b.Membership?.ContractId != contractId);
            var verdict = _placement.Check(block.Scheduled, block.PresetType, block.Membership, [newContract], calendar, running, now, _clock.TimeZone);
            if (verdict.Outcome == PlacementOutcome.TimeTaken)
                return EditNenContractResult.Fail("A Focus block already exists here.");
            if (verdict.Outcome != PlacementOutcome.Allowed)
                return EditNenContractResult.Fail("A block falls outside the contract's hours — move it inside them.");
        }

        // Only now, after every fallible construction has already succeeded, mutate anything.
        // Every member is replaced, Bonus blocks included — a bridge until Gap 68's adoption
        // (Gap 97, decision 17); the drafting seed carries them all, so none is lost unseen.
        var oldLinkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);
        foreach (var block in oldLinkedBlocks)
            await _blocks.DeleteAsync(block.Id, ct);

        await _contracts.DeleteAsync(contractId, ct);

        foreach (var block in preparation.Blocks)
            await _blocks.AddAsync(block, ct);

        await _contracts.AddAsync(newContract, ct);

        return EditNenContractResult.Ok(contractId);
    }
}
