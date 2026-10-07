using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public sealed class ContractBlockBatchPreparer : IContractBlockBatchPreparer
{
    public ContractBlockBatchPreparation Prepare(
        IReadOnlyList<(Guid PresetId, TimeSlot Slot, bool IsFocus, string? Title)> specs,
        Guid? contractId,
        UserTimeZone timeZone)
    {
        // Drafted blocks are the only Committed ones (Gap 97); no contract yet means a preview.
        var membership = contractId is { } id ? new ContractMembership(id, ContractRole.Committed) : null;

        var blocks = specs
            .Select(spec => spec.IsFocus
                ? (PlannedBlock)FocusBlock.Create(Guid.NewGuid(), spec.PresetId, spec.Slot, spec.Title, membership)
                : BackgroundBlock.Create(Guid.NewGuid(), spec.PresetId, spec.Slot, spec.Title, membership))
            .ToList();

        var focusBlocks = blocks.OfType<FocusBlock>().OrderBy(b => b.Scheduled).ToList();
        for (var i = 1; i < focusBlocks.Count; i++)
        {
            if (focusBlocks[i].OverlapsWith(focusBlocks[i - 1]))
                return ContractBlockBatchPreparation.Fail(ContractBlockBatchOutcome.OverlapsWithinBatch);
        }

        var plannedFocusHoursByDay = timeZone.HoursPerDay(focusBlocks.Select(b => b.Scheduled));

        return ContractBlockBatchPreparation.Ok(blocks, plannedFocusHoursByDay);
    }
}
