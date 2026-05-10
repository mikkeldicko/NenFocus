using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System;

namespace NenFocus.Domain.Entities;

public class Session : BaseEntity
{
    public SessionGoal Goal { get; private set; } = null!;
    public SessionDuration Duration { get; private set; } = null!;
    public SessionStatus Status { get; private set; }
    public Guid BlockProfileId { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public BreakSettings? BreakSettings { get; private set; }
    public int BreaksTaken { get; private set; }

    /// <summary>
    /// UTC timestamp of when the current break started.
    /// Null when not on break. Reset to null when resuming.
    /// </summary>
    public DateTime? BreakStartedAt { get; private set; }

    /// <summary>
    /// Optional post-session reflection text entered by the user.
    /// </summary>
    public string? Reflection { get; private set; }

    /// <summary>
    /// User rating of the session (e.g., 1-5).
    /// </summary>
    public int? Rating { get; private set; }

    /// <summary>
    /// JSON-serialized dictionary of blocked app and website attempts.
    /// </summary>
    public string? BlockedAttemptsSerialized { get; private set; }

    private Session() { } // Required for EF Core

    public static Session Create(
        SessionGoal goal,
        SessionDuration duration,
        Guid blockProfileId,
        BreakSettings? breakSettings = null)
    {
        return new Session
        {
            Goal = goal,
            Duration = duration,
            BlockProfileId = blockProfileId,
            BreakSettings = breakSettings,
            Status = SessionStatus.NotStarted
        };
    }

    public void Start()
    {
        if (Status != SessionStatus.NotStarted)
            throw new InvalidOperationException("Session has already been started.");

        Status = SessionStatus.Active;
        StartedAt = DateTime.UtcNow;
    }

    public void StartBreak()
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException("Can only take a break during an active session.");

        if (BreakSettings is null)
            throw new InvalidOperationException("Cannot start a break — no break settings configured for this session.");

        Status = SessionStatus.OnBreak;
        BreakStartedAt = DateTime.UtcNow;
        BreaksTaken++;
    }

    public void ResumeFromBreak()
    {
        if (Status != SessionStatus.OnBreak)
            throw new InvalidOperationException("Session is not currently on a break.");

        Status = SessionStatus.Active;
        BreakStartedAt = null;
    }

    public void Complete()
    {
        if (Status == SessionStatus.Completed)
            throw new InvalidOperationException("Session is already completed.");

        Status = SessionStatus.Completed;
        EndedAt = DateTime.UtcNow;
        BreakStartedAt = null;
    }

    public void SetReflection(string? reflection, int rating)
    {
        if (Status != SessionStatus.Completed)
            throw new InvalidOperationException("Reflection can only be set on a completed session.");

        Reflection = string.IsNullOrWhiteSpace(reflection) ? null : reflection.Trim();
        Rating = rating;
    }

    public void SetBlockedAttempts(string? serializedAttempts)
    {
        BlockedAttemptsSerialized = serializedAttempts;
    }

    public TimeSpan? GetElapsedTime()
    {
        if (StartedAt is null) return null;
        return (EndedAt ?? DateTime.UtcNow) - StartedAt.Value;
    }

    public TimeSpan? GetRemainingTime()
    {
        var elapsed = GetElapsedTime();
        if (elapsed is null) return null;
        var remaining = Duration.Value - elapsed.Value;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public void SyncState(int breaksTaken, DateTime? breakStartedAt)
    {
        // This allows us to restore the state from a DTO or external source
        BreaksTaken = breaksTaken;
        BreakStartedAt = breakStartedAt;
    }
}
