using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public sealed class ContractBlockBatchPreparer : IContractBlockBatchPreparer
{
    private readonly IPlannedFocusHoursCalculator _calculator;

    public ContractBlockBatchPreparer(IPlannedFocusHoursCalculator calculator)
    {
        _calculator = calculator;
    }

    public ContractBlockBatchPreparation Prepare(
        IReadOnlyList<(Guid PresetId, DateTime Start, DateTime End, bool IsFocus, string? Title)> specs,
        Guid? contractId)
    {
        var blocks = specs
            .Select(spec => spec.IsFocus
                ? (PlannedBlock)FocusBlock.Create(Guid.NewGuid(), spec.PresetId, spec.Start, spec.End, spec.Title, contractId)
                : BackgroundBlock.Create(Guid.NewGuid(), spec.PresetId, spec.Start, spec.End, spec.Title, contractId))
            .ToList();

        var focusBlocks = blocks.OfType<FocusBlock>().OrderBy(b => b.Start).ToList();
        for (var i = 1; i < focusBlocks.Count; i++)
        {
            if (focusBlocks[i].OverlapsWith(focusBlocks[i - 1]))
                return ContractBlockBatchPreparation.Fail(ContractBlockBatchOutcome.OverlapsWithinBatch);
        }

        var plannedFocusHoursByDay = _calculator.Calculate(focusBlocks.Select(b => (b.Start, b.End)));

        return ContractBlockBatchPreparation.Ok(blocks, focusBlocks, plannedFocusHoursByDay);
    }
}
