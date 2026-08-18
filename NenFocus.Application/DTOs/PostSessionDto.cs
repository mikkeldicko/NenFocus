using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PostSessionDto
{
    public Guid SessionId { get; init; }
    public SessionStatus Status { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }
    // double, not int: a session under a minute (or a grace-period credit that isn't a
    // round number) must not get truncated to a whole minute before it ever reaches the
    // TimeSpan below — DurationToStringConverter needs the real sub-minute value to show
    // seconds instead of "0".
    public double PlannedDurationMinutes { get; init; }
    public double ActualDurationMinutes { get; init; }
    public int BreaksTaken { get; init; }

    public TimeSpan ActualDuration => TimeSpan.FromMinutes(ActualDurationMinutes);
    public TimeSpan PlannedDuration => TimeSpan.FromMinutes(PlannedDurationMinutes);

    public IReadOnlyDictionary<string, int> BlockedWebsiteAttempts { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> BlockedAppAttempts { get; init; }
        = new Dictionary<string, int>();

    public int TotalBlockedAttempts { get; init; }
}
