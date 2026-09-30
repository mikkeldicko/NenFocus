using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record SessionSummaryDto
{
    public Guid Id { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
    public int PlannedDurationMinutes { get; init; }
    public int ActualDurationMinutes { get; init; }
    public Moment StartedAt { get; init; }
    public Moment? EndedAt { get; init; }
    public SessionStatus Status { get; init; }
    public int BreaksTaken { get; init; }
    public int TotalBreakMinutes { get; init; }
}
