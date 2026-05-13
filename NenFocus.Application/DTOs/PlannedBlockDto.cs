using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Represents a scheduled Ritual or Background Block on the Planner calendar.
/// Returned by GetPlannedBlocksForDayQuery and GetForRangeQuery.
/// </summary>
public sealed record PlannedBlockDto
{
    public Guid Id { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;

    public BlockType BlockType { get; init; }

    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>
    /// True if this block has an allowlist-mode profile attached.
    /// Used by the calendar to render the "AL" chip.
    /// </summary>
    public bool IsAllowlistMode { get; init; }

    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}