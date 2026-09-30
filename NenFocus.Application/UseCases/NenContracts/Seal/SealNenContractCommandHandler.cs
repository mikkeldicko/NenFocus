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

        // Advanced-only "no fixed floor" choice: for Daily Flexibility/Locked each day's floor
        // follows the plan (that day's own planned hours). For Period Time Budget it means
        // something different — no per-day obligation at all, only a whole-period total
        // (ContractFloor), derived the same way: whatever was drafted becomes the committed target.
        DailyFloorRule floorRule;
        ContractFloor? contractFloor = null;

        if (command.FixedDailyFloorHours is { } fixedHours)
        {
            floorRule = new DailyFloorRule.Fixed(new DailyFloor(fixedHours));
        }
        else if (command.StrictnessRule == StrictnessRule.PeriodTimeBudget)
        {
            floorRule = new DailyFloorRule.None();
            contractFloor = new ContractFloor(preparation.PlannedFocusHoursByDay.Values.Sum());
        }
        else
        {
            floorRule = new DailyFloorRule.FollowsPlan();
        }

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
                strictnessRule: command.StrictnessRule,
                calendar: calendar,
                floorRule: floorRule,
                changeWindow: null,
                allowCancellation: command.AllowCancellation,
                plannedFocusHoursByDay: preparation.PlannedFocusHoursByDay,
                sealedAt: now,
                timeZone: _clock.TimeZone,
                contractPresetId: command.ContractPresetId,
                contractFloor: contractFloor,
                redemptionPlacementPolicy: command.RedemptionPlacementPolicy,
                keepBlockingDuringBreaks: command.KeepBlockingDuringBreaks);
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
            var verdict = _placement.Check(block.Scheduled, block.PresetType, contract, calendar, running, now, _clock.TimeZone);
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
