namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A running session's timing (Gap 92, A4): its run, the breaks laid over it, and whether its
/// contract keeps blocking through them. <see cref="ReadingAt"/> is the one answer to "where is the
/// session right now" — the Main page's countdown, enforcement's "is blocking lifted" and the
/// session's own break answers all read it, so the screen and the blocking can't disagree.
/// Worked out from the session's stored fields (<c>Session.Timeline</c>), never stored itself.
/// </summary>
public sealed record SessionTimeline
{
    public SessionTimeline(TimeSlot run, BreakSchedule breaks, bool keepsBlockingDuringBreaks)
    {
        if (!breaks.FitsWithin(run.Length))
            throw new ArgumentException("Every break must end before the run does.", nameof(breaks));

        Run = run;
        Breaks = breaks;
        KeepsBlockingDuringBreaks = keepsBlockingDuringBreaks;
    }

    public TimeSlot Run { get; }
    public BreakSchedule Breaks { get; }
    public bool KeepsBlockingDuringBreaks { get; }

    /// <summary>Where the session is at <paramref name="at"/>.</summary>
    public TimelineReading ReadingAt(Moment at)
    {
        if (Run.HasEnded(at))
            return new TimelineReading(TimeSpan.Zero, 1.0, null, TimeSpan.Zero, 0, Breaks.BreaksStartedBy(Run.Length),
                LiftsBlocking: false, HasEnded: true);

        var offset = Run.SinceStart(at);
        var current = Breaks.BreakAt(offset);
        return new TimelineReading(
            Run.RemainingAt(at),
            Run.ProgressAt(at),
            current,
            current?.RemainingAt(offset) ?? TimeSpan.Zero,
            current?.ProgressAt(offset) ?? 0,
            Breaks.BreaksStartedBy(offset),
            LiftsBlocking: current is not null && !KeepsBlockingDuringBreaks,
            HasEnded: false);
    }

    /// <summary>Every break boundary crossed in (from, to] — what the monitor passes on each tick.</summary>
    public IReadOnlyList<BreakTransition> TransitionsBetween(Moment from, Moment to) =>
        Breaks.TransitionsBetween(Run.SinceStart(from), Run.SinceStart(to));

    /// <summary>Where each break sits on the run, for the progress bar's tick marks.</summary>
    public IReadOnlyList<BreakMark> Marks =>
        Breaks.Breaks.Select(b => new BreakMark(b.Start / Run.Length, b.Kind)).ToList();
}

