using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;
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
    private readonly IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool> _checkOverlap;
    private readonly ISystemClock _clock;

    public EditNenContractCommandHandler(
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

    public async Task<EditNenContractResult> HandleAsync(EditNenContractCommand command, CancellationToken ct = default)
    {
        var oldContract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (oldContract is null)
            return EditNenContractResult.Fail("Contract not found.");

        if (oldContract.Status != NenContractStatus.Scheduled)
            return EditNenContractResult.Fail("Only a Scheduled contract can be edited.");

        // Reuses the SAME id — literal identity preservation, not a new contract.
        var contractId = command.ContractId;

        var preparation = _batchPreparer.Prepare(
            command.Blocks.Select(spec => (spec.PresetId, spec.Start, spec.End, spec.IsFocus, spec.Title)).ToList(),
            contractId);

        if (preparation.Outcome == ContractBlockBatchOutcome.OverlapsWithinBatch)
            return EditNenContractResult.Fail("Two Focus blocks in this schedule overlap each other.");

        if (await _checkOverlap.HandleAsync(
                new CheckFocusBlockBatchOverlapQuery(preparation.FocusBlocks, ExcludeContractId: contractId), ct))
            return EditNenContractResult.Fail("A Focus block already exists here.");

        // Mirrors SealNenContractCommandHandler's identical branch exactly — see its
        // comment for the Period Time Budget / ContractFloor rationale.
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
            return EditNenContractResult.Fail(ex.Message);
        }

        // Same check as SealNenContractCommandHandler — every block inside its day's window.
        if (preparation.Blocks.Any(b => !newContract.AllowsBlockPlacement(_clock.ToLocal(b.Start), _clock.ToLocal(b.End))))
            return EditNenContractResult.Fail("A block falls outside the contract's hours — move it inside them.");

        // Only now, after every fallible construction has already succeeded, mutate anything.
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
