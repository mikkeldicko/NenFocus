using NenFocus.Application.DTOs;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeRedemptionAttribution;

/// <summary>
/// ContractId null with an empty Entries list means no eligible deficit was found — the
/// block stays an ordinary, uncommitted block. A non-null ContractId always comes paired
/// with at least one entry; there is no "attached to a contract but covers nothing" state.
/// Slot is the block's final window — capped to what the entries actually cover (Gap 88
/// phase 8, the one place the cap is computed); WasCapped says whether that shortening is
/// meaningful enough to tell the user about.
/// </summary>
public sealed record RedemptionAttributionResult(
    Guid? ContractId,
    IReadOnlyList<RedemptionEntryDto> Entries,
    TimeSlot Slot,
    bool WasCapped = false);
