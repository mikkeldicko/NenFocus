namespace NenFocus.Application.DTOs;

/// <summary>
/// One already-scheduled block belonging to a contract opened for editing — carries enough
/// to re-seed the Planner's drafting-mode tiles (PresetName included so the tile can render
/// without a further lookup, mirroring PlannedBlockDto's own "ready to display" shape).
/// </summary>
public sealed record NenContractEditBlockDto
{
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool IsFocus { get; init; }
    public string? Title { get; init; }
}
