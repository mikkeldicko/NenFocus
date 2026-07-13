using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.NenContracts.Extend;

public sealed class ExtendNenContractCommandHandler : ICommandHandler<ExtendNenContractCommand, ExtendNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IContractBlockBatchPreparer _batchPreparer;
    private readonly IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool> _checkOverlap;
    private readonly ISystemClock _clock;

    public ExtendNenContractCommandHandler(
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

    public async Task<ExtendNenContractResult> HandleAsync(ExtendNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null)
            return ExtendNenContractResult.Fail("Contract not found.");

        var preparation = _batchPreparer.Prepare(
            command.Blocks.Select(spec => (spec.PresetId, spec.Start, spec.End, spec.IsFocus, spec.Title)).ToList(),
            command.ContractId);

        if (preparation.Outcome == ContractBlockBatchOutcome.OverlapsWithinBatch)
            return ExtendNenContractResult.Fail("Two Focus blocks in this extension overlap each other.");

        if (await _checkOverlap.HandleAsync(new CheckFocusBlockBatchOverlapQuery(preparation.FocusBlocks), ct))
            return ExtendNenContractResult.Fail("A Focus block already exists here.");

        var overrides = contract.DailyFloorPolicy.Default is not null
            ? DailyFloorPolicy.RestDayExceptions(command.RestDays)
            : DailyFloorPolicy.PerDayExceptionsFrom(preparation.PlannedFocusHoursByDay);

        try
        {
            contract.Extend(command.NewEndDate, preparation.PlannedFocusHoursByDay, _clock.UtcNow, overrides);
        }
        catch (ArgumentException ex)
        {
            // Status/newEndDate-ordering are pre-checked by the validator; this is only
            // reachable for a new day planned below its resolved floor.
            return ExtendNenContractResult.Fail(ex.Message);
        }

        foreach (var block in preparation.Blocks)
            await _blocks.AddAsync(block, ct);

        await _contracts.SaveAsync(contract, ct);

        return ExtendNenContractResult.Ok();
    }
}
