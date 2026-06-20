namespace NenFocus.Application.DTOs;

/// <summary>
/// Contains all data displayed on the post-session screen.
/// Built by EndSessionCommand when a session completes.
/// </summary>
public sealed record PostSessionDto
{
    public Guid SessionId { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }
    public int PlannedDurationMinutes { get; init; }
    public int ActualDurationMinutes { get; init; }
    public int BreaksTaken { get; init; }

    public TimeSpan ActualDuration => TimeSpan.FromMinutes(ActualDurationMinutes);
    public TimeSpan PlannedDuration => TimeSpan.FromMinutes(PlannedDurationMinutes);

    /// <summary>
    /// Blocked app attempts during the session, keyed by process name.
    /// E.g. { "Spotify": 3, "Discord": 1 }
    /// </summary>
    /// 
    public IReadOnlyDictionary<string, int> BlockedWebsiteAttempts { get; init; }
      = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> BlockedAppAttempts { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Total number of times any blocked app was killed.</summary>
    public int TotalBlockedAttempts { get; init; }
  

}
