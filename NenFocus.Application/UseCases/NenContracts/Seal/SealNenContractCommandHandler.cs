using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.NenContracts.Seal;

public sealed class SealNenContractCommandHandler : ICommandHandler<SealNenContractCommand, SealNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IContractBlockBatchPreparer _batchPreparer;
    private readonly ISessionRepository _sessions;
    private readonly IBlockPlacementChecker _placement;
    private readonly ISystemClock _clock;

    public SealNenContractCommandHandler(
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

    public async Task<SealNenContractResult> HandleAsync(SealNenContractCommand command, CancellationToken ct = default)
    {
        var contractId = Guid.NewGuid();

        var preparation = _batchPreparer.Prepare(
            command.Blocks.Select(spec => (spec.PresetId, spec.Slot, spec.IsFocus, spec.Title)).ToList(),
            contractId,
            _clock.TimeZone);

        if (preparation.Outcome == ContractBlockBatchOutcome.OverlapsWithinBatch)
            return SealNenContractResult.Fail("Two Focus blocks in this schedule overlap each other.");

        var now = _clock.Now;

        var rules = new ContractRules(
            command.StrictnessRule,
            command.FixedDailyFloorHours is { } fixedHours ? new DailyFloor(fixedHours) : null,
            command.AllowCancellation,
            command.KeepBlockingDuringBreaks,
            command.RedemptionPlacementPolicy,
            command.BonusBlocksFollowStrictness);

        // The rules say how days get their floor (ContractRules.FloorRule). With no per-day
        // obligation, whatever was drafted becomes the committed whole-period target.
        var floorRule = rules.FloorRule;
        var contractFloor = floorRule is DailyFloorRule.None
            ? new ContractFloor(preparation.PlannedFocusHoursByDay.Values.Sum())
            : null;

        var workHours = command.WorkHoursStart is { } workHoursStart && command.WorkHoursEnd is { } workHoursEnd
            ? new WorkHours(workHoursStart, workHoursEnd)
            : null;

        var localNow = _clock.TimeZone.ToCalendarTime(now);

        NenContract contract;
        try
        {
            // The command carries no start time yet (Open Gap W adds the wizard's "When"
            // step) — until then the start is the suggested one for StartDate.
            var calendar = ContractCalendar.Generate(
                localStart: ContractCalendar.SuggestedStart(command.StartDate, workHours, command.NoWorkHoursDays, localNow),
                endDate: command.EndDate,
                endTime: null,
                workHours: workHours,
                noWorkHoursDays: command.NoWorkHoursDays,
                floorRule: floorRule,
                restDays: command.RestDays,
                plannedFocusHoursByDay: preparation.PlannedFocusHoursByDay);

            contract = NenContract.Seal(
                id: contractId,
                name: command.Name,
                goal: new NenContractGoal(command.Goal),
                rules: rules,
                calendar: calendar,
                changeWindow: null,
                plannedFocusHoursByDay: preparation.PlannedFocusHoursByDay,
                sealedAt: now,
                timeZone: _clock.TimeZone,
                contractPresetId: command.ContractPresetId,
                contractFloor: contractFloor);
        }
        catch (ArgumentException ex)
        {
            return SealNenContractResult.Fail(ex.Message);
        }

        // Every block asks the one placement answer, BlockPlacementChecker.Check (Gap 88 phase 8):
        // not in the past → inside its day's window of this contract → free for Focus, against the
        // calendar as it will be and the running session. Seal's own batch sentences, by outcome.
        var running = await _sessions.GetActiveSessionsAsync(ct);
        foreach (var block in preparation.Blocks)
        {
            var calendar = await _blocks.GetForRangeAsync(block.Scheduled, ct);
            var verdict = _placement.Check(block.Scheduled, block.PresetType, block.Membership, [contract], calendar, running, now, _clock.TimeZone);
            if (verdict.Outcome == PlacementOutcome.TimeTaken)
                return SealNenContractResult.Fail("A Focus block already exists here.");
            if (verdict.Outcome != PlacementOutcome.Allowed)
                return SealNenContractResult.Fail("A block falls outside the contract's hours — move it inside them.");
        }

        foreach (var block in preparation.Blocks)
            await _blocks.AddAsync(block, ct);

        await _contracts.AddAsync(contract, ct);

        return SealNenContractResult.Ok(contractId);
    }
}
