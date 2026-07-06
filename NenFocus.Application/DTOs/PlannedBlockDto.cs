using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PlannedBlockDto
{
    public Guid Id { get; init; }
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;

    public BlockPresetType BlockPresetType { get; init; }
    public WebsiteFilterType WebsiteFilterType { get; init; }

    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public bool IsCompleted { get; init; }

    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}
