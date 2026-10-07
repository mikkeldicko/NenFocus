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
    public Moment? StartedAt { get; private set; }
    public Moment? EndAt { get; private set; }
    public Moment? EndedAt { get; private set; }

    // Persisted as the SessionBreaks child table (EF OwnsMany); exposed as a BreakSchedule.
    private List<SessionBreak> _breaks = [];

    /// <summary>
    /// This session's breaks as offsets from StartedAt — the single source of truth for
    /// "is it a break right now" (Gap 63). A session is one continuous Active run; a
    /// break is only a stretch of time on this schedule, never a status. Set at Start;
    /// once a break has started it can never change — only breaks still in the future
    /// could ever be revised. Trimmed at Complete to what actually happened, so the
    /// same list is the permanent history record.
    /// </summary>
    public BreakSchedule BreakSchedule => new(_breaks);

    /// <summary>
    /// Snapshot of the name of the BreakTimerPreset the schedule came from (e.g.
    /// "Classic"), copied at Start so renaming or deleting the preset never rewrites
    /// history. Null when the session has no breaks at all.
    /// </summary>
    public string? BreakPresetName { get; private set; }

    /// <summary>
    /// Whether this session's preset stays enforced through its breaks (Gap 64). Resolved
    /// once at Start from the contract its planned block is committed to (false otherwise)
    /// and never looked up again — the contract can't change while its sessions run.
    /// </summary>
    public bool KeepBlockingDuringBreaks { get; private set; }
    public string? Reflection { get; private set; }
    public Moment? PostSessionAcknowledgedAt { get; private set; }
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
            Status = SessionStatus.NotStarted,
            TemporaryBlockedApps = temporaryBlockedApps ?? [],
            TemporaryBlockedSites = temporaryBlockedSites ?? []
        };
    }

    /// <summary>
    /// How long this session will run if started at <paramref name="startedAt"/> — until the
    /// end of <paramref name="plannedWindow"/> (a Planned session's block, <c>block.Scheduled</c>)
    /// or for its Duration. The one definition Start and a caller building a BreakSchedule
    /// share, so a schedule is always laid over exactly the run it will be checked against.
    /// </summary>
    public TimeSpan RunLength(Moment startedAt, TimeSlot? plannedWindow = null) => EndOfRun(startedAt, plannedWindow) - startedAt;

    private Moment EndOfRun(Moment startedAt, TimeSlot? plannedWindow) =>
        plannedWindow?.End ?? startedAt + Duration.Value;

    public void Start(
        Moment startedAt,
        TimeSlot? plannedWindow = null,
        BreakSchedule? breakSchedule = null,
        string? breakPresetName = null,
        bool keepBlockingDuringBreaks = false)
    {
        if (Status != SessionStatus.NotStarted)
            throw new InvalidOperationException(
                "Session has already been started.");

        var schedule = breakSchedule ?? BreakSchedule.None;
        if (!schedule.FitsWithin(RunLength(startedAt, plannedWindow)))
            throw new ArgumentException(
                "Every break must end before the session does.", nameof(breakSchedule));

        Status = SessionStatus.Active;
        StartedAt = startedAt;
        EndAt = EndOfRun(startedAt, plannedWindow);
        _breaks = schedule.Breaks.ToList();
        // A label only ever describes breaks that exist — a preset that turned out to
        // have no room for a single break leaves the session honestly labelled as none.
        BreakPresetName = schedule.IsEmpty ? null : breakPresetName;
        KeepBlockingDuringBreaks = keepBlockingDuringBreaks;
        Raise(new SessionStartedEvent(Id, BlockPresetId, Goal.Value, startedAt));
    }

    /// <summary>
    /// This session's run, its breaks and its keep-blocking rule, once started — the one answer to "where is
    /// it at a moment" (<see cref="SessionTimeline.ReadingAt"/>) that the break questions below, enforcement and
    /// the Main page all read (Gap 92, A4). Worked out from the stored fields, never stored itself.
    /// </summary>
    public SessionTimeline? Timeline =>
        StartedAt is { } startedAt && EndAt is { } endAt
            ? new SessionTimeline(new TimeSlot(startedAt, endAt), BreakSchedule, KeepBlockingDuringBreaks)
            : null;

    /// <summary>True while <paramref name="at"/> falls inside one of this running
    /// session's break slots — whether it *is* a break; see LiftsBlockingAt for what it does.</summary>
    public bool IsOnBreakAt(Moment at) => CurrentBreakAt(at) is not null;

    /// <summary>True when this session's own preset should stop being enforced at
    /// <paramref name="at"/>: it is inside a break slot and its contract doesn't keep
    /// blocking through breaks. The one question PolicyEngine and the UI ask (Gap 64).</summary>
    public bool LiftsBlockingAt(Moment at) =>
        Status == SessionStatus.Active && Timeline is { } timeline && timeline.ReadingAt(at).LiftsBlocking;

    /// <summary>The break slot covering <paramref name="at"/>, or null while working or
    /// when the session isn't running.</summary>
    public SessionBreak? CurrentBreakAt(Moment at) =>
        Status == SessionStatus.Active && Timeline is { } timeline ? timeline.ReadingAt(at).CurrentBreak : null;

    /// <summary>Every break boundary this session crossed in (from, to] — what the
    /// clock-watching monitor asks each tick instead of tracking phases itself.</summary>
    public IReadOnlyList<BreakTransition> BreakTransitionsBetween(Moment from, Moment to) =>
        Timeline?.TransitionsBetween(from, to) ?? [];

    /// <summary>Breaks begun by <paramref name="at"/>.</summary>
    public int BreaksTakenBy(Moment at) => Timeline?.ReadingAt(at).BreaksTaken ?? 0;

    /// <summary>Final break count, once the session has ended; 0 before then.</summary>
    public int BreaksTaken => EndedAt is { } endedAt ? BreaksTakenBy(endedAt) : 0;

    /// <summary>
    /// Ends the session. The recorded end is capped at EndAt: a session can never run
    /// longer than planned, however late its end is noticed (sleep, a restart, a skipped
    /// clock). The break schedule is trimmed to what actually happened by then.
    /// </summary>
    public void Complete(Moment endedAt)
    {
        if (Status == SessionStatus.Completed)
            throw new InvalidOperationException(
                "Session is already completed.");

        var actualEnd = EndAt is { } plannedEnd && endedAt > plannedEnd ? plannedEnd : endedAt;

        Status = SessionStatus.Completed;
        EndedAt = actualEnd;
        if (StartedAt is { } startedAt)
            _breaks = BreakSchedule.TrimTo(actualEnd - startedAt).Breaks.ToList();
        Raise(new SessionEndedEvent(Id, actualEnd));
    }

    /// <summary>A planned session whose block's window closed without the Ritual: it holds that
    /// window (<paramref name="scheduled"/> — the block's own slot) as its start and end.</summary>
    public void Miss(TimeSlot scheduled)
    {
        if (Origin != SessionOrigin.Planned)
            throw new InvalidOperationException("Only a planned session can be missed.");

        if (Status != SessionStatus.NotStarted)
            throw new InvalidOperationException("Only a session that never started can be missed.");

        Status = SessionStatus.Missed;
        StartedAt = scheduled.Start;
        EndedAt = scheduled.End;
        Raise(new SessionMissedEvent(Id, scheduled.Start, scheduled.End));
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
    public void MarkPostSessionAcknowledged(Moment acknowledgedAt)
    {
        if (Status != SessionStatus.Completed && Status != SessionStatus.Missed)
            throw new InvalidOperationException(
                "Post-session can only be acknowledged for a completed or missed session.");

        PostSessionAcknowledgedAt = acknowledgedAt;
    }

    /// <summary>
    /// The time this session occupies while it runs — StartedAt to its scheduled end — and null
    /// once it isn't running. What Focus placement must keep clear (Gap 88). A running planned
    /// session covers the same time as its block; checking both is harmless.
    /// </summary>
    public TimeSlot? ReservedSlot =>
        Status == SessionStatus.Active && StartedAt is { } started && EndAt is { } end
            ? new TimeSlot(started, end)
            : null;

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
    public TimeSpan? GetElapsedTime(Moment asOf)
    {
        // A missed session never ran — its StartedAt/EndedAt hold the scheduled
        // window (inherited from the originating block), not real elapsed time.
        if (Status == SessionStatus.Missed) return TimeSpan.Zero;
        if (StartedAt is null) return null;
        var raw = (EndedAt ?? asOf) - StartedAt.Value;
        return WholeMinutes.RoundDown(raw).AsTimeSpan;
    }

    /// <summary>
    /// How long this ended session counted for (Gap 95): a planned session counts its block's
    /// credit (<see cref="FocusBlock.CreditedSlot"/> at this session's end — grace included,
    /// nothing for a missed block), so Post-Session shows the same number the contract counts;
    /// otherwise the whole minutes it ran. A planned session whose block is gone counts what it
    /// ran. Asked only of an ended session; the block passed must be its own.
    /// </summary>
    public TimeSpan CountedTime(FocusBlock? originatingBlock)
    {
        if (EndedAt is not { } endedAt)
            throw new InvalidOperationException("Only an ended session has a counted time.");
        if (originatingBlock is not null && originatingBlock.Id != OriginatingBlockId)
            throw new ArgumentException("This block is not the one this session was started for.", nameof(originatingBlock));

        return originatingBlock is not null
            ? originatingBlock.CreditedSlot(endedAt)?.Length ?? TimeSpan.Zero
            : GetElapsedTime(endedAt) ?? TimeSpan.Zero;
    }

    public TimeSpan? GetRemainingTime(Moment asOf)
    {
        if (EndAt is null) return null;
        var remaining = EndAt.Value - asOf;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}