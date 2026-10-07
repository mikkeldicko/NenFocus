using NenFocus.Domain.ValueObjects;
namespace NenFocus.Application.DTOs;

/// <summary>
/// One preset's worth of outstanding, today-coverable deficit under a contract, with a
/// suggested tile the "Cover deficit" banner button can pre-fill the block form with. The
/// suggestion is a starting point, not a placement — whether it fits is Save's job (Gap 88).
/// </summary>
public sealed record SuggestedRedemptionPlacementDto
{
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public WholeMinutes TotalRemainingDeficit { get; init; }
    public TimeSlot Suggested { get; init; } = null!;
}
