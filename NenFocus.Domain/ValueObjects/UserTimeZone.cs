namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The user's time zone and its rules (DST included) — the one place a <see cref="Moment"/>
/// becomes a calendar time (what the user's clock shows) and back. Nothing else converts.
/// Only SystemClock builds it from the machine's zone (architecture test); tests build it from
/// a fixed zone. Named UserCalendar until 2026-09-30. Gap 88.
/// </summary>
public sealed record UserTimeZone
{
    private readonly TimeZoneInfo _zone;

    public UserTimeZone(TimeZoneInfo zone) => _zone = zone ?? throw new ArgumentNullException(nameof(zone));

    /// <summary>What the user's clock shows at this moment. Kind is Unspecified: a calendar time, not a moment.</summary>
    public DateTime ToCalendarTime(Moment moment) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(moment.Utc, _zone), DateTimeKind.Unspecified);

    /// <summary>
    /// The moment a calendar time refers to; its Kind is ignored. A clock time that doesn't exist
    /// (spring forward) or happens twice (fall back) is read with the zone's standard offset —
    /// the same answer DateTime.ToUniversalTime gave before, and it never throws.
    /// </summary>
    public Moment ToMoment(DateTime calendarTime)
    {
        var clockTime = DateTime.SpecifyKind(calendarTime, DateTimeKind.Unspecified);
        return Moment.FromUtc(clockTime - _zone.GetUtcOffset(clockTime));
    }

    /// <summary>The user's calendar date at this moment.</summary>
    public DateOnly DateOf(Moment moment) => DateOnly.FromDateTime(ToCalendarTime(moment));
}
