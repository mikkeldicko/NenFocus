using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;

namespace NenFocus.Domain.Services;

/// <summary>
/// Builds the set of PlannedBlocks proposed alongside a Nen Contract mutation (Seal or
/// Extend) and validates them against each other before anything is persisted. Stateless
/// and synchronous — nothing here touches a repository; the caller is responsible for the
/// separate against-already-persisted-blocks check (BlockPlacementChecker.CheckFree, asked by Seal/Edit contract — Gap 88),
/// which needs IO and so cannot live in Domain.
/// </summary>
public interface IContractBlockBatchPreparer
{
    ContractBlockBatchPreparation Prepare(
        IReadOnlyList<(Guid PresetId, TimeSlot Slot, bool IsFocus, string? Title)> specs,
        Guid? contractId,
        UserTimeZone timeZone);
}
