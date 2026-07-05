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
    public Guid BlockProfileId { get; private set; }
    public Guid? OriginatingBlockId { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public BreakSettings? BreakSettings { get; private set; }
    public int BreaksTaken { get; private set; }
    public DateTime? BreakStartedAt { get; private set; }
    public string? Reflection { get; private set; }
    public IReadOnlyList<string> TemporaryBlockedApps { get; private set; } = [];
    public IReadOnlyList<string> TemporaryBlockedSites { get; private set; } = [];

    private Session() { } // EF Core

    public static Session Create(
        Guid id,
        SessionGoal goal,
        SessionDuration duration,
        Guid blockProfileId,
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
            BlockProfileId = blockProfileId,
            Origin = origin,
            OriginatingBlockId = originatingBlockId,
            BreakSettings = breakSettings,
            Status = SessionStatus.NotStarted,
            TemporaryBlockedApps = temporaryBlockedApps ?? [],
            TemporaryBlockedSites = temporaryBlockedSites ?? []
        };
    }

    public void Start(DateTime startedAt)
    {
        if (Status != SessionStatus.NotStarted)
            throw new InvalidOperationException(
                "Session has already been started.");

        Status = SessionStatus.Active;
        StartedAt = startedAt;
        Raise(new SessionStartedEvent(Id, BlockProfileId, Goal.Value, startedAt));
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

    public TimeSpan? GetElapsedTime(DateTime asOf)
    {
        // A missed session never ran — its StartedAt/EndedAt hold the scheduled
        // window (inherited from the originating block), not real elapsed time.
        if (Status == SessionStatus.Missed) return TimeSpan.Zero;
        if (StartedAt is null) return null;
        return (EndedAt ?? asOf) - StartedAt.Value;
    }

    public TimeSpan? GetRemainingTime(DateTime asOf)
    {
        var elapsed = GetElapsedTime(asOf);
        if (elapsed is null) return null;
        var remaining = Duration.Value - elapsed.Value;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public void SyncState(int breaksTaken, DateTime? breakStartedAt)
    {
        BreaksTaken = breaksTaken;
        BreakStartedAt = breakStartedAt;
    }
}