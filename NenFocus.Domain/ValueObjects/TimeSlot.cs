using System.Linq.Expressions;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A span of real time, [Start, End): it includes its start and not its end, so at exactly End
/// the slot has ended and the next may start — the one boundary convention, written only here.
/// Blocks and sessions hold one; callers ask it questions instead of comparing Start/End, which
/// are Domain-only (Gap 88, option C).
/// </summary>
public sealed record TimeSlot : IComparable<TimeSlot>
{
    public TimeSlot(Moment start, Moment end)
    {
        if (end <= start)
            throw new ArgumentException("A time slot must end after it starts.", nameof(end));
        Start = start;
        End = end;
    }

    /// <summary>The slot, or null when <paramref name="end"/> isn't after <paramref name="start"/> —
    /// for input that another rule reports (a form's "end after start"), so callers never compare
    /// the two themselves.</summary>
    public static TimeSlot? TryCreate(Moment start, Moment end) => end > start ? new TimeSlot(start, end) : null;

    // For EF only: it maps Start/End by name onto the owner's own columns (NenFocusDbContext).
    private TimeSlot() { }

    internal Moment Start { get; }
    internal Moment End { get; }

    public TimeSpan Length => End - Start;

    /// <summary>True when the two share at least one moment; touching (one ends as the other starts) isn't overlapping.</summary>
    public bool Overlaps(TimeSlot other) => Start < other.End && other.Start < End;

    public bool Contains(Moment moment) => Start <= moment && moment < End;

    /// <summary>True when the whole of <paramref name="other"/> lies inside this slot; sharing a start or an end is inside.</summary>
    public bool Contains(TimeSlot other) => Start <= other.Start && other.End <= End;

    /// <summary>The one "has it started / is it in the past" rule.</summary>
    public bool HasStarted(Moment asOf) => Start <= asOf;

    public bool HasEnded(Moment asOf) => End <= asOf;

    /// <summary>The smallest slot containing both — e.g. the whole span of a split's two halves.</summary>
    public static TimeSlot Enclosing(TimeSlot a, TimeSlot b) =>
        new(a.Start < b.Start ? a.Start : b.Start, a.End > b.End ? a.End : b.End);

    /// <summary>The same slot, starting at <paramref name="start"/> instead (e.g. a late start). Throws if that leaves nothing.</summary>
    public TimeSlot From(Moment start) => new(start, End);

    /// <summary>The same slot, ending at <paramref name="end"/> instead (e.g. an early end). Throws if that leaves nothing.</summary>
    public TimeSlot Until(Moment end) => new(Start, end);

    /// <summary>Time until the slot starts — negative once it has.</summary>
    public TimeSpan StartsIn(Moment asOf) => Start - asOf;

    /// <summary>Time since the slot started — negative before it has (the mirror of <see cref="StartsIn"/>).</summary>
    public TimeSpan SinceStart(Moment asOf) => asOf - Start;

    /// <summary>Time left until the slot ends — zero once it has.</summary>
    public TimeSpan RemainingAt(Moment asOf) => asOf < End ? End - asOf : TimeSpan.Zero;

    /// <summary>How far through the slot <paramref name="asOf"/> is, from 0 (not started) to 1 (ended).</summary>
    public double ProgressAt(Moment asOf) => Math.Clamp(SinceStart(asOf) / Length, 0.0, 1.0);

    /// <summary>
    /// The slot from <paramref name="start"/> exactly <paramref name="minutes"/> long. Whole minutes, so
    /// nothing is rounded and nothing can be left short (Gap 33; Gap 92, V10).
    /// </summary>
    public static TimeSlot Covering(Moment start, WholeMinutes minutes)
    {
        if (minutes <= WholeMinutes.Zero)
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "There must be something to cover.");
        return new TimeSlot(start, start + minutes.AsTimeSpan);
    }

    /// <summary>By start, then end.</summary>
    public int CompareTo(TimeSlot? other)
    {
        if (other is null) return 1;
        var byStart = Start.CompareTo(other.Start);
        return byStart != 0 ? byStart : End.CompareTo(other.End);
    }

    /// <summary>
    /// The same rules as the methods above, as expressions a repository hands to the database —
    /// so the SQL filter *is* the Domain's rule, never a copy of it (Gap 88, option C). Written
    /// only as repositories need them.
    /// </summary>
    public static class Filters
    {
        /// <summary>SQL form of <see cref="TimeSlot.Overlaps"/>.</summary>
        public static Expression<Func<TimeSlot, bool>> Overlaps(TimeSlot other)
        {
            var otherStart = other.Start;
            var otherEnd = other.End;
            return slot => slot.Start < otherEnd && otherStart < slot.End;
        }

        /// <summary>
        /// SQL form of <see cref="TimeSlot.Contains(Moment)"/>, for a moment an entity holds
        /// on its own (a session's start): true when it falls inside <paramref name="slot"/>.
        /// A moment that isn't set is never inside.
        /// </summary>
        public static Expression<Func<Moment?, bool>> Contains(TimeSlot slot)
        {
            var start = slot.Start;
            var end = slot.End;
            return moment => moment >= start && moment < end;
        }

        /// <summary>SQL form of <see cref="TimeSlot.HasEnded"/>.</summary>
        public static Expression<Func<TimeSlot, bool>> HasEnded(Moment asOf) => slot => slot.End <= asOf;

        /// <summary>Sort key: by start.</summary>
        public static Expression<Func<TimeSlot, Moment>> ByStart { get; } = slot => slot.Start;
    }
}
