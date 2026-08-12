using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;

/// <summary>
/// Returns true if any of the given (not-yet-persisted) Focus blocks overlaps a Focus
/// block that already exists in the store. In-batch overlap (the proposed blocks against
/// each other) is a separate, pure concern already resolved by IContractBlockBatchPreparer
/// before this query is dispatched — this only answers the IO-dependent half of the check.
/// ExcludeContractId omits any persisted block belonging to that contract from the check —
/// used when editing a contract, so its own about-to-be-replaced blocks (not yet actually
/// deleted from the database at query time) never collide with their own replacements.
/// </summary>
public sealed record CheckFocusBlockBatchOverlapQuery(
    IReadOnlyList<FocusBlock> FocusBlocks,
    Guid? ExcludeContractId = null) : IQuery<bool>;
