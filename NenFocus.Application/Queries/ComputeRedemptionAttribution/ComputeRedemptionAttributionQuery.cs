using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.ComputeRedemptionAttribution;

/// <summary>
/// Whether a Focus block spanning [BlockStart, BlockEnd) with preset PresetId should be
/// linked to any outstanding deficit, and under which contract — the single source of
/// truth both CreatePlannedBlockCommandHandler and UpdatePlannedBlockCommandHandler use
/// to attach RedemptionEntries automatically, with no client-driven selection involved.
///
/// ContractId null means "search every currently in-effect contract for an eligible
/// match" (the ordinary new-block case); a value scopes the search to that one contract
/// only (an edit recomputing against a block's own already-known contract, or a caller
/// that already knows which contract applies). ExcludeBlockId serves two purposes on an
/// edit: it keeps the block being edited from ever matching itself as a missed candidate,
/// and — passed through as GetNenContractDeficitsQuery.ExcludeCoverageFromBlockId — it
/// excludes the block's own stale, pre-edit RedemptionEntries from the "already covered"
/// sum for whatever it's covering, so the block doesn't see its own prior contribution as
/// someone else's coverage and shrink its own new entry toward zero on every edit.
/// </summary>
public sealed record ComputeRedemptionAttributionQuery(
    Guid? ContractId,
    Guid PresetId,
    DateTime BlockStart,
    DateTime BlockEnd,
    Guid? ExcludeBlockId) : IQuery<RedemptionAttributionResult>;
