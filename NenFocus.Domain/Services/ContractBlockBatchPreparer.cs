using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

/// <summary>
/// Builds the set of Blocks proposed alongside a Nen Contract mutation (Seal or
/// Extend) and validates them against each other before anything is persisted. Stateless
/// and synchronous — nothing here touches a repository; the caller is responsible for the
/// separate against-already-persisted-blocks check (BlockPlacementChecker.CheckFree, asked by Seal/Edit contract — Gap 88),
/// which needs IO and so cannot live in Domain.
/// </summary>
public sealed class ContractBlockBatchPreparer
{
    public ContractBlockBatchPreparation Prepare(
        IReadOnlyList<(Guid PresetId, TimeSlot Slot, BlockPresetType Type, string? Title)> specs,
        Guid? contractId,
        UserTimeZone timeZone)
    {
        // Drafted blocks are the only Committed ones (Gap 97); no contract yet means a preview.
        var membership = contractId is { } id ? new ContractMembership(id, ContractRole.Committed) : null;

        var blocks = specs
            .Select(spec => spec.Type == BlockPresetType.Focus
                ? (Block)FocusBlock.Create(Guid.NewGuid(), spec.PresetId, spec.Slot, spec.Title, membership)
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
