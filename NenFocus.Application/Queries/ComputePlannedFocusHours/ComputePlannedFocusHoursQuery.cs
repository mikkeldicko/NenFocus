using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

/// <summary>
/// Computes planned Focus hours per calendar day from a contract's already-persisted
/// blocks (if any) merged with not-yet-saved draft ranges — the same calculation
/// whether it's live wizard feedback (ContractId null, drafts only), a live edit on an
/// already-sealed contract (ContractId set, drafts is the one block being dragged), or
/// the final commit at Seal (ContractId null, drafts is the whole finished schedule).
/// </summary>
/// <param name="ContractId">Null when the contract doesn't exist yet (the Seal wizard).</param>
/// <param name="DraftFocusBlockRanges">Not-yet-persisted Focus block ranges to include.</param>
/// <param name="ExcludeBlockId">
/// A persisted block to omit from the contract's saved blocks — set this to the block
/// being actively edited so its in-progress draft range isn't double-counted alongside
/// its still-saved original.
/// </param>
public sealed record ComputePlannedFocusHoursQuery(
    Guid? ContractId,
    IReadOnlyList<TimeSlot> DraftFocusBlockRanges,
    Guid? ExcludeBlockId = null) : IQuery<IReadOnlyDictionary<DateOnly, double>>;
