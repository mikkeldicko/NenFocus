using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PlannedBlockDto
{
    public Guid Id { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;

    public BlockType BlockType { get; init; }

    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>True if this block has an allowlist-mode profile attached.</summary>
    public bool IsAllowlistMode { get; init; }

    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}
