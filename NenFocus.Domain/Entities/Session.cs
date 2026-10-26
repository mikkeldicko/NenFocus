using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class Session : AggregateRoot
{
    public Guid Id { get; private set; }
    public SessionGoal Goal { get; private set; } = null!;
    public SessionStatus Status { get; private set; }
    public Guid OriginatingBlockId { get; private set; }

    // The run's own times (Gap 89 leftover 2): Domain-only, exposed through the answers below
    // (Timeline, Recorded, ReservedSlot, IsDueForAutoEnd), never as raw moments.
    internal Moment? StartedAt { get; private set; }
    internal Moment? EndAt { get; private set; }
    internal Moment? EndedAt { get; private set; }

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
    /// once at Start from the contract its block belongs to (false otherwise) and never
    /// looked up again — the contract can't change while its sessions run.
    /// </summary>
    public bool KeepBlockingDuringBreaks { get; private set; }

    /// <summary>
    /// The run's own words on how it went, and whether its Post-Session screen was dismissed —
    /// one value object, shared with a missed block and a contract (Gap 114, D3). The trim/blank
    /// rule lives on the value object; the "only once ended" check lives here.
    /// </summary>
    public Reflection Reflection { get; private set; } = new Reflection(null);

    public IReadOnlyList<BlockedApp> TemporaryBlockedApps { get; private set; } = [];
    public IReadOnlyList<Website> TemporaryBlockedSites { get; private set; } = [];

    private Session() { } // EF Core

    /// <summary>
    /// Starts the session — the one creation path (Gap 114, D6). Every session belongs to a Focus
    /// block (<paramref name="block"/> — the booking), which supplies the run's planned end; the
    /// session is the run. The block's arrival is stamped here, the one writer for both booking
    /// types. Created already Active: there is no "not yet started" session, since a session is a
    /// visit that has begun. The break schedule is laid over exactly the block's slot.
    /// </summary>
    public static Session Start(
        Guid id,
        SessionGoal goal,
        FocusBlock block,
        Moment startedAt,
        IReadOnlyList<BlockedApp>? temporaryBlockedApps = null,
        IReadOnlyList<Website>? temporaryBlockedSites = null,
        BreakSchedule? breakSchedule = null,
        string? breakPresetName = null,
        bool keepBlockingDuringBreaks = false)
    {
        var schedule = breakSchedule ?? BreakSchedule.None;
        if (!schedule.FitsWithin(block.Scheduled.Length))
            throw new ArgumentException(
                "Every break must end before the session does.", nameof(breakSchedule));

        block.MarkRitualCompleted(startedAt);

        var session = new Session
        {
            Id = id,
            Goal = goal,
            Status = SessionStatus.Active,
            OriginatingBlockId = block.Id,
            StartedAt = startedAt,
            EndAt = block.Scheduled.End,
            _breaks = schedule.Breaks.ToList(),
            // A label only ever describes breaks that exist — a preset that turned out to
            // have no room for a single break leaves the session honestly labelled as none.
            BreakPresetName = schedule.IsEmpty ? null : breakPresetName,
            KeepBlockingDuringBreaks = keepBlockingDuringBreaks,
            TemporaryBlockedApps = temporaryBlockedApps ?? [],
            TemporaryBlockedSites = temporaryBlockedSites ?? []
        };
        session.Raise(new SessionStartedEvent(id, block.BlockPresetId, goal.Value, startedAt));
        return session;
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

    /// <summary>
    /// Its time is up at <paramref name="at"/> and nothing has ended it yet — the clock's reason
    /// to auto-end it (Gap 89 Slice A). The same "ended" the monitor's reading reports.
    /// </summary>
    public bool IsDueForAutoEnd(Moment at) =>
        Status == SessionStatus.Active && Timeline is { } timeline && timeline.ReadingAt(at).HasEnded;

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
    /// The time this session's record covers: its run. Null while it hasn't ended. Not its
    /// Timeline, which is the planned run (to EndAt). Worked out from the stored fields, never
    /// stored itself (Gap 89 Slice C, F7).
    /// </summary>
    public TimeSlot? Recorded =>
        StartedAt is { } startedAt && EndedAt is { } endedAt ? new TimeSlot(startedAt, endedAt) : null;

    /// <summary>
    /// Whether it may end at <paramref name="at"/>: it has started, and its end there — capped at EndAt,
    /// as Complete records it — falls after its start. An ended session always spans some time. The one
    /// rule End early asks and Complete enforces, so the check and the saved end can't differ.
    /// </summary>
    public bool CanEndAt(Moment at) => StartedAt is { } startedAt && CappedEnd(at) > startedAt;

    /// <summary>The end Complete records for <paramref name="endedAt"/>: never later than EndAt — a session can
    /// never run longer than planned, however late its end is noticed (sleep, a restart, a skipped clock).</summary>
    private Moment CappedEnd(Moment endedAt) => EndAt is { } plannedEnd && endedAt > plannedEnd ? plannedEnd : endedAt;

    /// <summary>
    /// Ends the session — the one ending, whether you ended it or its time ran out (Gap 89
    /// Slice A). The recorded end is capped at EndAt: a session can never run longer than
    /// planned, however late its end is noticed (sleep, a restart, a skipped clock). The break
    /// schedule is trimmed to what actually happened by then, and the block is asked to freeze its
    /// result with that same end, always, early or on time (Gap 114, D6). The block is asked first,
    /// so a stamp it refuses leaves the session running. It must end after it started
    /// (<see cref="CanEndAt"/>, Gap 89 Slice C) — callers ask that first; this throw is the last guard.
    /// </summary>
    public void Complete(Moment endedAt, FocusBlock? originatingBlock)
    {
        if (Status == SessionStatus.Completed)
            throw new InvalidOperationException(
                "Session is already completed.");
        if (originatingBlock is not null && originatingBlock.Id != OriginatingBlockId)
            throw new ArgumentException("This block is not the one this session was started for.", nameof(originatingBlock));
        if (!CanEndAt(endedAt))
            throw new InvalidOperationException("A session can't end before it started.");

        var actualEnd = CappedEnd(endedAt);

        originatingBlock?.MarkSessionEnded(actualEnd);

        Status = SessionStatus.Completed;
        EndedAt = actualEnd;
        if (StartedAt is { } startedAt)
            _breaks = BreakSchedule.TrimTo(actualEnd - startedAt).Breaks.ToList();
        Raise(new SessionEndedEvent(Id, actualEnd));
    }

    public void SetReflection(string? reflection)
    {
        if (Status != SessionStatus.Completed)
            throw new InvalidOperationException(
                "Reflection can only be set on a completed session.");

        Reflection = this.Reflection.WithText(reflection);
    }

    /// <summary>
    /// Stamped when the user dismisses the post-session screen (clicking Done),
    /// regardless of whether they wrote a reflection — mirrors RitualCompletedAt's
    /// idiom. A blank reflection is a valid, deliberate outcome and must not be
    /// confused with "never saw the screen at all"; this is the one fact that
    /// distinguishes the two, letting a startup query find any completed session whose
    /// post-session screen was never dismissed.
    /// </summary>
    public void MarkPostSessionAcknowledged(Moment acknowledgedAt)
    {
        if (Status != SessionStatus.Completed)
            throw new InvalidOperationException(
                "Post-session can only be acknowledged for a completed session.");

        Reflection = this.Reflection.Acknowledged(acknowledgedAt);
    }

    /// <summary>
    /// The time this session occupies while it runs — StartedAt to its scheduled end — and null
    /// once it isn't running. What Focus placement must keep clear (Gap 88). A running session
    /// covers the same time as its block; checking both is harmless.
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
    /// the timeline and tick on their own; this method exists purely for "how much does
    /// this count for," where a whole minute is already the smallest unit that means
    /// anything.
    /// </summary>
    public TimeSpan? GetElapsedTime(Moment asOf)
    {
        if (StartedAt is null) return null;
        var raw = (EndedAt ?? asOf) - StartedAt.Value;
        return WholeMinutes.RoundDown(raw).AsTimeSpan;
    }

    /// <summary>
    /// How long this ended session counted for (Gap 95): its block's credit (<see cref="FocusBlock.CreditedSlot"/>
    /// at this session's end — grace included), so Post-Session shows the same number the contract
    /// counts. A session whose block is gone counts what it ran. Asked only of an ended session;
    /// the block passed must be its own.
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
