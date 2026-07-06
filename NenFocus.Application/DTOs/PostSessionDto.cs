using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PostSessionDto
{
    public Guid SessionId { get; init; }
    public SessionStatus Status { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }
    public int PlannedDurationMinutes { get; init; }
    public int ActualDurationMinutes { get; init; }
    public int BreaksTaken { get; init; }

    public TimeSpan ActualDuration => TimeSpan.FromMinutes(ActualDurationMinutes);
    public TimeSpan PlannedDuration => TimeSpan.FromMinutes(PlannedDurationMinutes);

    public IReadOnlyDictionary<string, int> BlockedWebsiteAttempts { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> BlockedAppAttempts { get; init; }
        = new Dictionary<string, int>();

    public int TotalBlockedAttempts { get; init; }
}
