using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The single source of truth for a session's breaks: an ordered list of break slots,
/// each an offset from the session's StartedAt (Gap 63). A session is one continuous
/// run punctuated by these slots — "is it a break right now" is always read off this
/// list against the clock, never kept as separate state.
///
/// Deliberately ignorant of how it was produced: a BreakTimerPreset's cadence
/// (BreakTimer.ScheduleFor) is one way, a user's hand-made list would be another. A
/// session that has finished keeps its schedule trimmed to what actually happened
/// (TrimTo), so the same list is also the permanent history record.
/// </summary>
public sealed class BreakSchedule : IEquatable<BreakSchedule>
{
    public static readonly BreakSchedule None = new([]);

    public IReadOnlyList<SessionBreak> Breaks { get; }

    public BreakSchedule(IEnumerable<SessionBreak> breaks)
    {
        var ordered = breaks.OrderBy(b => b.Start).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            // Strictly after: every break must be followed by some work before the next.
            if (ordered[i].Start <= ordered[i - 1].End)
                throw new ArgumentException(
                    "Breaks must not overlap or touch; there must be work between them.", nameof(breaks));
        }

        Breaks = ordered;
    }

    public bool IsEmpty => Breaks.Count == 0;

    /// <summary>True when every break ends strictly before <paramref name="runLength"/>,
    /// i.e. each one runs to completion and leaves real work time after it.</summary>
    public bool FitsWithin(TimeSpan runLength) => Breaks.All(b => b.End < runLength);

    /// <summary>The break covering <paramref name="offset"/> ([Start, End)), or null during work.</summary>
    public SessionBreak? BreakAt(TimeSpan offset) =>
        Breaks.FirstOrDefault(b => offset >= b.Start && offset < b.End);

    public int BreaksStartedBy(TimeSpan offset) => Breaks.Count(b => b.Start <= offset);

    /// <summary>Every break boundary in the half-open window (from, to], in order. A
    /// backwards window (the clock moved back) crosses nothing.</summary>
    public IReadOnlyList<BreakTransition> TransitionsBetween(TimeSpan from, TimeSpan to)
    {
        if (to <= from) return [];

        var transitions = new List<BreakTransition>();
        foreach (var b in Breaks)
        {
            if (b.Start > from && b.Start <= to)
                transitions.Add(new BreakTransition(BreakTransitionKind.Started, b.Kind, b.Start));
            if (b.End > from && b.End <= to)
                transitions.Add(new BreakTransition(BreakTransitionKind.Ended, b.Kind, b.End));
        }
        return transitions;
    }

    /// <summary>What actually happened by <paramref name="offset"/>: breaks that hadn't
    /// started are dropped, one in progress is cut short there.</summary>
    public BreakSchedule TrimTo(TimeSpan offset) =>
        new(Breaks
            .Where(b => b.Start < offset)
            .Select(b => b.End <= offset ? b : new SessionBreak(b.Kind, b.Start, offset)));

    public bool Equals(BreakSchedule? other) =>
        other is not null && Breaks.SequenceEqual(other.Breaks);

    public override bool Equals(object? obj) => Equals(obj as BreakSchedule);

    public override int GetHashCode() =>
        Breaks.Aggregate(0, (hash, b) => HashCode.Combine(hash, b));
}
