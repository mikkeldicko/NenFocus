using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;
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
    private readonly IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool> _checkOverlap;
    private readonly ISystemClock _clock;

    public SealNenContractCommandHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IContractBlockBatchPreparer batchPreparer,
        IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool> checkOverlap,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _batchPreparer = batchPreparer;
        _checkOverlap = checkOverlap;
        _clock = clock;
    }

    public async Task<SealNenContractResult> HandleAsync(SealNenContractCommand command, CancellationToken ct = default)
    {
        var contractId = Guid.NewGuid();

        var preparation = _batchPreparer.Prepare(
            command.Blocks.Select(spec => (spec.PresetId, spec.Start, spec.End, spec.IsFocus, spec.Title)).ToList(),
            contractId);

        if (preparation.Outcome == ContractBlockBatchOutcome.OverlapsWithinBatch)
            return SealNenContractResult.Fail("Two Focus blocks in this schedule overlap each other.");

        if (await _checkOverlap.HandleAsync(new CheckFocusBlockBatchOverlapQuery(preparation.FocusBlocks), ct))
            return SealNenContractResult.Fail("A Focus block already exists here.");

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

        var localNow = _clock.ToLocal(_clock.UtcNow);

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
                localSealedAt: localNow,
                contractPresetId: command.ContractPresetId,
                contractFloor: contractFloor,
                redemptionPlacementPolicy: command.RedemptionPlacementPolicy,
                keepBlockingDuringBreaks: command.KeepBlockingDuringBreaks);
        }
        catch (ArgumentException ex)
        {
            return SealNenContractResult.Fail(ex.Message);
        }

        // Every block must fit its day's window — which also refuses a block before the
        // contract's start, i.e. one already in the past.
        if (preparation.Blocks.Any(b => !contract.AllowsBlockPlacement(_clock.ToLocal(b.Start), _clock.ToLocal(b.End))))
            return SealNenContractResult.Fail("A block falls outside the contract's hours — move it inside them.");

        foreach (var block in preparation.Blocks)
            await _blocks.AddAsync(block, ct);

        await _contracts.AddAsync(contract, ct);

        return SealNenContractResult.Ok(contractId);
    }
}
