namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// An amount of time counted in whole minutes — the unit focus is accounted in: credit, deficits,
/// redemption coverage, and a planned block's length (Gap 92, V10). A fraction can't be built, so
/// arithmetic is exact and no "close enough" tolerance is ever needed. The only ways in from a
/// TimeSpan say what they do: <see cref="RoundDown"/> (credit: never over-credit) and
/// <see cref="TryExact"/> (must already be whole). An amount, not a place on the calendar — that is
/// <see cref="TimeSlot"/>.
/// </summary>
public readonly record struct WholeMinutes : IComparable<WholeMinutes>
{
    private WholeMinutes(long minutes) => Minutes = minutes;

    /// <summary>The count of minutes, never negative.</summary>
    public long Minutes { get; }

    public static WholeMinutes Zero => default;

    public static WholeMinutes Of(long minutes) => minutes >= 0
        ? new(minutes)
        : throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "An amount of minutes can't be negative.");

    /// <summary>The whole minutes in <paramref name="time"/>, dropping any partial one; zero for none or negative.</summary>
    public static WholeMinutes RoundDown(TimeSpan time) => time > TimeSpan.Zero ? new(time.Ticks / TimeSpan.TicksPerMinute) : Zero;

    /// <summary><paramref name="time"/> as whole minutes, or null when it holds a partial minute (or is negative).</summary>
    public static WholeMinutes? TryExact(TimeSpan time) =>
        time >= TimeSpan.Zero && time.Ticks % TimeSpan.TicksPerMinute == 0 ? new WholeMinutes(time.Ticks / TimeSpan.TicksPerMinute) : null;

    /// <summary>Exactly this many minutes as a TimeSpan — to add to a Moment.</summary>
    public TimeSpan AsTimeSpan => TimeSpan.FromTicks(Minutes * TimeSpan.TicksPerMinute);

    /// <summary>The hours this amount makes, for hour-based figures (a day's target, a banner).</summary>
    public double Hours => Minutes / 60.0;

    public static WholeMinutes operator +(WholeMinutes a, WholeMinutes b) => new(a.Minutes + b.Minutes);

    public static WholeMinutes operator -(WholeMinutes a, WholeMinutes b) => a.Minutes >= b.Minutes
        ? new(a.Minutes - b.Minutes)
        : throw new InvalidOperationException("Can't take more minutes away than there are — use Less to stop at zero.");

    /// <summary>What is left once <paramref name="other"/> is taken away, never below zero.</summary>
    public WholeMinutes Less(WholeMinutes other) => Minutes > other.Minutes ? new(Minutes - other.Minutes) : Zero;

    public static WholeMinutes Min(WholeMinutes a, WholeMinutes b) => a.Minutes <= b.Minutes ? a : b;

    public static bool operator <(WholeMinutes a, WholeMinutes b) => a.Minutes < b.Minutes;
    public static bool operator >(WholeMinutes a, WholeMinutes b) => a.Minutes > b.Minutes;
    public static bool operator <=(WholeMinutes a, WholeMinutes b) => a.Minutes <= b.Minutes;
    public static bool operator >=(WholeMinutes a, WholeMinutes b) => a.Minutes >= b.Minutes;

    public int CompareTo(WholeMinutes other) => Minutes.CompareTo(other.Minutes);

    public override string ToString() => $"{Minutes} min";
}
