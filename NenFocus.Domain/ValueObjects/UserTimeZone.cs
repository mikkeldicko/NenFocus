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

    /// <summary>
    /// The real time the user's calendar days <paramref name="firstDay"/> to
    /// <paramref name="lastDay"/> cover: local midnight to the local midnight after the last day
    /// — the mirror of <see cref="SplitByDay"/>, so a daylight-saving day is 23 or 25 hours.
    /// What a "these days" query hands a repository, which only knows moments (Gap 97).
    /// </summary>
    public TimeSlot SlotCovering(DateOnly firstDay, DateOnly lastDay)
    {
        if (lastDay < firstDay)
            throw new ArgumentException("The last day can't be before the first.", nameof(lastDay));

        return new TimeSlot(
            ToMoment(firstDay.ToDateTime(TimeOnly.MinValue)),
            ToMoment(lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue)));
    }

    /// <summary>The real time one calendar day covers — see <see cref="SlotCovering(DateOnly, DateOnly)"/>.</summary>
    public TimeSlot SlotCovering(DateOnly day) => SlotCovering(day, day);

    /// <summary>Both ends of a slot as the user's clock shows them — for display; a slot's own Start/End are Domain-only.</summary>
    public (DateTime Start, DateTime End) ToCalendarTimes(TimeSlot slot) => (ToCalendarTime(slot.Start), ToCalendarTime(slot.End));

    /// <summary>
    /// The slot cut at the user's local midnights, one part per calendar day it touches, in real
    /// time — so a daylight-saving night's day is 23 or 25 hours, and "how much falls on each day"
    /// is always right. A slot ending exactly at midnight has no part on the next day.
    /// </summary>
    public IEnumerable<(DateOnly Date, TimeSlot Part)> SplitByDay(TimeSlot slot)
    {
        var cursor = slot.Start;
        while (cursor < slot.End)
        {
            var date = DateOf(cursor);
            var nextMidnight = ToMoment(date.AddDays(1).ToDateTime(TimeOnly.MinValue));
            var partEnd = nextMidnight < slot.End ? nextMidnight : slot.End;

            yield return (date, new TimeSlot(cursor, partEnd));
            cursor = partEnd;
        }
    }
}
