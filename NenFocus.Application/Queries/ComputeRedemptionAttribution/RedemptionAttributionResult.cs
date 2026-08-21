using NenFocus.Application.DTOs;

namespace NenFocus.Application.Queries.ComputeRedemptionAttribution;

/// <summary>
/// ContractId null with an empty Entries list means no eligible deficit was found — the
/// block stays an ordinary, uncommitted block. A non-null ContractId always comes paired
/// with at least one entry; there is no "attached to a contract but covers nothing" state.
/// </summary>
public sealed record RedemptionAttributionResult(
    Guid? ContractId,
    IReadOnlyList<RedemptionEntryDto> Entries);
