using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

public sealed record PostSessionDto
{
    public Guid SessionId { get; init; }
    public SessionStatus Status { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
    public Moment StartedAt { get; init; }
    public Moment EndedAt { get; init; }
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

    /// <summary>
    /// The one builder (Gap 95): EndSession, MarkFocusBlockMissed and GetUnacknowledgedPostSessions
    /// all call it. It only maps the ended session and groups its attempts — how long the session
    /// counted for is the session's own answer (<see cref="Session.CountedTime"/>).
    /// </summary>
    public static PostSessionDto From(
        Session session, FocusBlock? originatingBlock, string presetName, IReadOnlyList<BlockedAttempt> attempts) => new()
    {
        SessionId = session.Id,
        Status = session.Status,
        Goal = session.Goal.Value,
        PresetName = presetName,
        StartedAt = session.StartedAt!.Value,
        EndedAt = session.EndedAt!.Value,
        PlannedDurationMinutes = session.Duration.Value.TotalMinutes,
        ActualDurationMinutes = session.CountedTime(originatingBlock).TotalMinutes,
        BreaksTaken = session.BreaksTaken,
        BlockedAppAttempts = CountByResource(attempts, BlockedAttemptType.App),
        BlockedWebsiteAttempts = CountByResource(attempts, BlockedAttemptType.Website),
        TotalBlockedAttempts = attempts.Count
    };

    private static IReadOnlyDictionary<string, int> CountByResource(IReadOnlyList<BlockedAttempt> attempts, BlockedAttemptType type) =>
        attempts.Where(a => a.AttemptType == type).GroupBy(a => a.ResourceName).ToDictionary(g => g.Key, g => g.Count());
}
