namespace NenFocus.Application.DTOs;

/// <summary>
/// One preset's worth of outstanding, today-coverable deficit under a contract, with a
/// suggested tile the "Cover deficit" banner button can create as-is. CanFullyCover false
/// means SuggestedEnd stops at the day's boundary (midnight or Work Hours close) short of
/// actually closing the full deficit — the caller should confirm with the user before
/// creating a knowingly-partial tile.
/// </summary>
public sealed record SuggestedRedemptionPlacementDto
{
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public double TotalRemainingDeficitMinutes { get; init; }
    public DateTime SuggestedStart { get; init; }
    public DateTime SuggestedEnd { get; init; }
    public bool CanFullyCover { get; init; }
}
