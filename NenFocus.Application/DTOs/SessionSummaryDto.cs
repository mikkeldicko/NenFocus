namespace NenFocus.Application.DTOs;

public sealed record SessionSummaryDto
{
    public Guid Id { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int PlannedDurationMinutes { get; init; }
    public int ActualDurationMinutes { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public bool Completed { get; init; }
    public int BreaksTaken { get; init; }
    public int TotalBreakMinutes { get; init; }
}
