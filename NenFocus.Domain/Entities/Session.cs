using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class Session : AggregateRoot
{
    public Guid Id { get; private set; }
    public SessionGoal Goal { get; private set; } = null!;
    public SessionDuration Duration { get; private set; } = null!;
    public SessionStatus Status { get; private set; }
    public SessionOrigin Origin { get; private set; }
    public Guid BlockPresetId { get; private set; }
    public Guid? OriginatingBlockId { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public BreakSettings? BreakSettings { get; private set; }
    public int BreaksTaken { get; private set; }
    public DateTime? BreakStartedAt { get; private set; }
    public string? Reflection { get; private set; }
    public DateTime? PostSessionAcknowledgedAt { get; private set; }
    public IReadOnlyList<string> TemporaryBlockedApps { get; private set; } = [];
    public IReadOnlyList<string> TemporaryBlockedSites { get; private set; } = [];

    private Session() { } // EF Core

    public static Session Create(
        Guid id,
        SessionGoal goal,
        SessionDuration duration,
        Guid blockPresetId,
        SessionOrigin origin,
        Guid? originatingBlockId = null,
        BreakSettings? breakSettings = null,
        IReadOnlyList<string>? temporaryBlockedApps = null,
        IReadOnlyList<string>? temporaryBlockedSites = null)
    {
        if (origin == SessionOrigin.Planned && originatingBlockId is null)
            throw new ArgumentException(
                "A planned session must have an originating block id.");

        if (origin == SessionOrigin.Spontaneous && originatingBlockId is not null)
            throw new ArgumentException(
                "A spontaneous session cannot have an originating block id.");

        return new Session
        {
            Id = id,
            Goal = goal,
            Duration = duration,
            BlockPresetId = blockPresetId,
            Origin = origin,
            OriginatingBlockId = originatingBlockId,
            BreakSettings = breakSettings,
            Status = SessionStatus.NotStarted,
            TemporaryBlockedApps = temporaryBlockedApps ?? [],
            TemporaryBlockedSites = temporaryBlockedSites ?? []
        };
    }

    public void Start(DateTime startedAt, DateTime? scheduledEnd = null)
    {
        if (Status != SessionStatus.NotStarted)
            throw new InvalidOperationException(
                "Session has already been started.");

        Status = SessionStatus.Active;
        StartedAt = startedAt;
        EndAt = scheduledEnd ?? startedAt + Duration.Value;
        Raise(new SessionStartedEvent(Id, BlockPresetId, Goal.Value, startedAt));
    }

    public void StartBreak(DateTime breakStartedAt)
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException(
                "Can only take a break during an active session.");

        if (BreakSettings is null)
            throw new InvalidOperationException(
                "Cannot start a break — no break settings configured.");

        Status = SessionStatus.OnBreak;
        BreakStartedAt = breakStartedAt;
        BreaksTaken++;
        Raise(new BreakStartedEvent(Id, BreaksTaken, breakStartedAt));
    }

    public void ResumeFromBreak()
    {
        if (Status != SessionStatus.OnBreak)
            throw new InvalidOperationException(
                "Session is not currently on a break.");

        Status = SessionStatus.Active;
        BreakStartedAt = null;
    }

    public void Complete(DateTime endedAt)
    {
        if (Status == SessionStatus.Completed)
            throw new InvalidOperationException(
                "Session is already completed.");

        Status = SessionStatus.Completed;
        EndedAt = endedAt;
        BreakStartedAt = null;
        Raise(new SessionEndedEvent(Id, endedAt));
    }

    public void Miss(DateTime scheduledStart, DateTime scheduledEnd)
    {
        if (Origin != SessionOrigin.Planned)
            throw new InvalidOperationException("Only a planned session can be missed.");

        if (Status != SessionStatus.NotStarted)
            throw new InvalidOperationException("Only a session that never started can be missed.");

        Status = SessionStatus.Missed;
        StartedAt = scheduledStart;
        EndedAt = scheduledEnd;
        Raise(new SessionMissedEvent(Id, scheduledStart, scheduledEnd));
    }

    public void SetReflection(string? reflection)
    {
        if (Status != SessionStatus.Completed && Status != SessionStatus.Missed)
            throw new InvalidOperationException(
                "Reflection can only be set on a completed or missed session.");

        Reflection = string.IsNullOrWhiteSpace(reflection)
            ? null
            : reflection.Trim();
    }

    /// <summary>
    /// Stamped when the user dismisses the post-session screen (clicking Done),
    /// regardless of whether they wrote a reflection — mirrors RitualCompletedAt/
    /// SessionEndedAt's idiom. A missed or blank reflection is a valid, deliberate
    /// outcome and must not be confused with "never saw the screen at all"; this is
    /// the one fact that distinguishes the two, letting a startup query find any
    /// Completed or Missed session whose post-session screen was never dismissed.
    /// </summary>
    public void MarkPostSessionAcknowledged(DateTime acknowledgedAt)
    {
        if (Status != SessionStatus.Completed && Status != SessionStatus.Missed)
            throw new InvalidOperationException(
                "Post-session can only be acknowledged for a completed or missed session.");

        PostSessionAcknowledgedAt = acknowledgedAt;
    }

    /// <summary>Mirrors PlannedBlock.HasEnded/NenContract.IsRestDay's naming idiom — a single
    /// named question over EndedAt so callers ask whether this session has actually finished
    /// (naturally or ended early) rather than null-checking the raw field themselves.</summary>
    public bool HasEnded => EndedAt.HasValue;

    /// <summary>
    /// Floored to whole minutes — the single source every accounting consumer (credited
    /// focus time, the deficit ledger, completed/bonus hours, Post-Session's duration,
    /// session history) reads elapsed time from, so none of them ever have to reconcile
    /// sub-minute click-timing noise from real StartedAt/EndedAt timestamps against each
    /// other. Deliberately not used by any live, second-by-second UI display — those read
    /// StartedAt/EndAt directly and tick on their own; this method exists purely for "how
    /// much does this count for," where a whole minute is already the smallest unit that
    /// means anything.
    /// </summary>
    public TimeSpan? GetElapsedTime(DateTime asOf)
    {
        // A missed session never ran — its StartedAt/EndedAt hold the scheduled
        // window (inherited from the originating block), not real elapsed time.
        if (Status == SessionStatus.Missed) return TimeSpan.Zero;
        if (StartedAt is null) return null;
        var raw = (EndedAt ?? asOf) - StartedAt.Value;
        return TimeSpan.FromMinutes(Math.Floor(raw.TotalMinutes));
    }

    public TimeSpan? GetRemainingTime(DateTime asOf)
    {
        if (EndAt is null) return null;
        var remaining = EndAt.Value - asOf;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public void SyncState(int breaksTaken, DateTime? breakStartedAt)
    {
        BreaksTaken = breaksTaken;
        BreakStartedAt = breakStartedAt;
    }
}