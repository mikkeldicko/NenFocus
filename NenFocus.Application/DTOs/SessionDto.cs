namespace NenFocus.Application.DTOs;

/// <summary>
/// Represents an active or completed session.
/// Returned by GetActiveSessionQuery and GetSessionHistoryQuery.
/// </summary>
public sealed record SessionDto
{
    public Guid Id { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int DurationMinutes { get; init; }
    public string Status { get; init; } = string.Empty; // "Active", "OnBreak", "Completed"
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public BreakSettingsDto? BreakSettings { get; init; }
    public int BreaksTaken { get; init; }
    public DateTime? BreakStartedAt { get; init; }
    public IReadOnlyList<string> TemporaryBlockedApps { get; init; } = [];
    public IReadOnlyList<string> TemporaryBlockedSites { get; init; } = [];
}