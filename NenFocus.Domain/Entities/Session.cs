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

    /// <summary>
    /// The frozen cadence copied by value from a BreakTimerPreset at Start — replaces
    /// BreakSettings, which only ever modeled a single fixed interval/length pair.
    /// Null means "No timer": this session runs start-to-finish with no breaks at
    /// all. Manual break-taking is retired app-wide — a break only ever starts
    /// automatically, driven by walking IBreakScheduleCalculator's phase timeline
    /// for this cadence against the session's own Duration.
    /// </summary>
    public BreakTimer? BreakTimer { get; private set; }
    public int BreaksTaken { get; private set; }
    public DateTime? BreakStartedAt { get; private set; }

    /// <summary>
    /// Which kind of break is currently active — set from StartBreak's breakKind
    /// parameter (itself resolved by the caller via IBreakScheduleCalculator, never
    /// re-derived here), cleared on ResumeFromBreak. Null whenever Status is not
    /// OnBreak.
    /// </summary>
    public SessionPhaseKind? CurrentBreakKind { get; private set; }
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
        BreakTimer? breakTimer = null,
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
            BreakTimer = breakTimer,
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

    /// <summary>
    /// Starts a break. breakKind is supplied by the caller (resolved via
    /// IBreakScheduleCalculator against this session's BreakTimer and Duration) —
    /// Session never re-derives which kind of break this is itself; it only records
    /// the answer. Breaks no longer start manually: the only caller is the
    /// automatic engine that notices a Work phase has elapsed.
    /// </summary>
    public void StartBreak(DateTime breakStartedAt, SessionPhaseKind breakKind)
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException(
                "Can only take a break during an active session.");

        if (BreakTimer is null)
            throw new InvalidOperationException(
                "Cannot start a break — no timer cadence configured.");

        if (breakKind == SessionPhaseKind.Work)
            throw new ArgumentException(
                "Break kind must be ShortBreak or LongBreak.", nameof(breakKind));

        Status = SessionStatus.OnBreak;
        BreakStartedAt = breakStartedAt;
        CurrentBreakKind = breakKind;
        BreaksTaken++;
        Raise(new BreakStartedEvent(Id, BreaksTaken, breakStartedAt));
    }

    public void ResumeFromBreak(DateTime resumedAt)
    {
        if (Status != SessionStatus.OnBreak)
            throw new InvalidOperationException(
                "Session is not currently on a break.");

        Status = SessionStatus.Active;
        BreakStartedAt = null;
        CurrentBreakKind = null;
        Raise(new BreakEndedEvent(Id, BreaksTaken, resumedAt));
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