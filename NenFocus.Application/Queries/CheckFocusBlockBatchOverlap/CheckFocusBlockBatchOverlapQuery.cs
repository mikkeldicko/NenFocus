using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;

/// <summary>
/// Returns true if any of the given (not-yet-persisted) Focus blocks overlaps a Focus
/// block that already exists in the store. In-batch overlap (the proposed blocks against
/// each other) is a separate, pure concern already resolved by IContractBlockBatchPreparer
/// before this query is dispatched — this only answers the IO-dependent half of the check.
/// </summary>
public sealed record CheckFocusBlockBatchOverlapQuery(IReadOnlyList<FocusBlock> FocusBlocks) : IQuery<bool>;
