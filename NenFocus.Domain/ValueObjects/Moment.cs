namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A point in time — always UTC, the same instant everywhere. Kept apart from calendar times
/// (what the user's clock shows) by the compiler: it has no .Date, .Hour, .DayOfWeek or
/// formatting, because answering those needs a time zone — ask <see cref="UserTimeZone"/>.
/// That is exactly the mix-up behind Gaps 65/87/96. Gap 88 (folded-in Gap 99).
/// </summary>
public readonly record struct Moment : IComparable<Moment>
{
    private readonly DateTime _utc;

    private Moment(DateTime utc) => _utc = utc;

    /// <summary>The raw UTC value, for the Domain's own conversions (<see cref="UserTimeZone"/>).</summary>
    internal DateTime Utc => DateTime.SpecifyKind(_utc, DateTimeKind.Utc);

    internal static Moment FromUtc(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    /// <summary>
    /// Storage entry: only the database converter, the machine clock and the tests' <c>At</c>
    /// helper make a moment from a DateTime (guarded by an architecture test).
    /// </summary>
    public static Moment FromStorage(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException($"A moment is stored as UTC; got a DateTime of kind {utc.Kind}.", nameof(utc));
        return new Moment(utc);
    }

    /// <summary>Storage exit — the only public way back to a DateTime (same guard as <see cref="FromStorage"/>).</summary>
    public DateTime ToStorage() => Utc;

    public int CompareTo(Moment other) => _utc.CompareTo(other._utc);

    public static bool operator <(Moment left, Moment right) => left._utc < right._utc;
    public static bool operator >(Moment left, Moment right) => left._utc > right._utc;
    public static bool operator <=(Moment left, Moment right) => left._utc <= right._utc;
    public static bool operator >=(Moment left, Moment right) => left._utc >= right._utc;

    public static Moment operator +(Moment moment, TimeSpan duration) => new(moment._utc + duration);
    public static Moment operator -(Moment moment, TimeSpan duration) => new(moment._utc - duration);
    public static TimeSpan operator -(Moment later, Moment earlier) => later._utc - earlier._utc;
}
