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

        // Advanced-only "no fixed floor" choice: for Daily Flexibility/Locked this is the
        // existing PerDay derivation (each day's floor = that day's own planned hours).
        // For Period Time Budget it means something different — no per-day obligation at
        // all, only a whole-period total (ContractFloor), derived the same way: whatever
        // was drafted becomes the committed target.
        DailyFloorPolicy dailyFloorPolicy;
        ContractFloor? contractFloor = null;

        if (command.FixedDailyFloorHours is { } fixedHours)
        {
            dailyFloorPolicy = DailyFloorPolicy.Fixed(new DailyFloor(fixedHours), command.RestDays);
        }
        else if (command.StrictnessRule == StrictnessRule.PeriodTimeBudget)
        {
            dailyFloorPolicy = DailyFloorPolicy.None;
            contractFloor = new ContractFloor(preparation.PlannedFocusHoursByDay.Values.Sum());
        }
        else
        {
            dailyFloorPolicy = DailyFloorPolicy.PerDay(preparation.PlannedFocusHoursByDay);
        }

        var workHoursPolicy = command.WorkHoursStart is { } workHoursStart && command.WorkHoursEnd is { } workHoursEnd
            ? new WorkHoursPolicy(new WorkHours(workHoursStart, workHoursEnd), WorkHoursPolicy.NoConstraintExceptions(command.NoWorkHoursDays))
            : null;

        NenContract contract;
        try
        {
            contract = NenContract.Seal(
                id: contractId,
                name: command.Name,
                goal: new NenContractGoal(command.Goal),
                startDate: command.StartDate,
                endDate: command.EndDate,
                strictnessRule: command.StrictnessRule,
                dailyFloorPolicy: dailyFloorPolicy,
                changeWindow: null,
                allowCancellation: command.AllowCancellation,
                plannedFocusHoursByDay: preparation.PlannedFocusHoursByDay,
                sealedAt: _clock.UtcNow,
                workHoursPolicy: workHoursPolicy,
                contractPresetId: command.ContractPresetId,
                contractFloor: contractFloor,
                redemptionPlacementPolicy: command.RedemptionPlacementPolicy);
        }
        catch (ArgumentException ex)
        {
            return SealNenContractResult.Fail(ex.Message);
        }

        foreach (var block in preparation.Blocks)
            await _blocks.AddAsync(block, ct);

        await _contracts.AddAsync(contract, ct);

        return SealNenContractResult.Ok(contractId);
    }
}
