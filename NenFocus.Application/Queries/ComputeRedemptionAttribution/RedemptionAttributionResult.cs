using NenFocus.Application.DTOs;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeRedemptionAttribution;

/// <summary>
/// What the block covers in the contract that was asked: empty Entries means no eligible
/// deficit — the block redeems nothing. Which contract the block belongs to is not this
/// query's answer (Gap 97): the caller already knows it.
/// Slot is the block's final window — capped to what the entries actually cover (Gap 88
/// phase 8, the one place the cap is computed); WasCapped says whether that shortening is
/// meaningful enough to tell the user about.
/// </summary>
public sealed record RedemptionAttributionResult(
    IReadOnlyList<RedemptionEntryDto> Entries,
    TimeSlot Slot,
    bool WasCapped = false);
