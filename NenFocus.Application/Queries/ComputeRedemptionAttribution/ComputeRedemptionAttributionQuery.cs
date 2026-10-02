using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeRedemptionAttribution;

/// <summary>
/// Whether a Focus block spanning [BlockStart, BlockEnd) with preset PresetId should be
/// linked to any outstanding deficit, and under which contract — the single source of
/// truth both CreatePlannedBlockCommandHandler and UpdatePlannedBlockCommandHandler use
/// to attach RedemptionEntries automatically, with no client-driven selection involved.
///
/// ContractId is the one contract asked about deficits (Gap 97): the contract the block sits
/// in for a new block (the caller asks IBlockPlacementChecker.GoverningContract first), or a
/// redemption block's own contract on an edit. A block inside no contract redeems nothing, so
/// this query is never asked for it. ExcludeBlockId serves two purposes on an
/// edit: it keeps the block being edited from ever matching itself as a missed candidate,
/// and — passed through as GetNenContractDeficitsQuery.ExcludeCoverageFromBlockId — it
/// excludes the block's own stale, pre-edit RedemptionEntries from the "already covered"
/// sum for whatever it's covering, so the block doesn't see its own prior contribution as
/// someone else's coverage and shrink its own new entry toward zero on every edit.
///
/// ExistingEntries carries forward the block's own already-attached entries (empty on a
/// brand-new block). Each is treated as an already-validated commitment: its amount
/// rescales with current reality (Rule 13's own principle, applied one level up — see
/// GetNenContractDeficitsQueryHandler), but it is never re-gated by
/// CheckRedemptionEligibility. Only capacity left over after carrying every existing
/// entry forward is eligible to originate a genuinely new claim, which *is* day-gated —
/// see the handler.
/// </summary>
public sealed record ComputeRedemptionAttributionQuery(
    Guid ContractId,
    Guid PresetId,
    TimeSlot Block,
    Guid? ExcludeBlockId,
    IReadOnlyList<RedemptionEntryDto>? ExistingEntries = null) : IQuery<RedemptionAttributionResult>
{
    public IReadOnlyList<RedemptionEntryDto> ExistingEntries { get; init; } = ExistingEntries ?? [];
}
