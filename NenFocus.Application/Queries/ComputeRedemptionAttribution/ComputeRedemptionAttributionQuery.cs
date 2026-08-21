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
/// that already knows which contract applies). ExcludeBlockId keeps a block being edited
/// from ever matching itself as a missed candidate.
/// </summary>
public sealed record ComputeRedemptionAttributionQuery(
    Guid? ContractId,
    Guid PresetId,
    DateTime BlockStart,
    DateTime BlockEnd,
    Guid? ExcludeBlockId) : IQuery<RedemptionAttributionResult>;
