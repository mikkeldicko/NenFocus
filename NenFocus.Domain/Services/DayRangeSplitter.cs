namespace NenFocus.Domain.Services;

/// <summary>
/// Splits a [start, end) range into one segment per calendar day it occupies — the shared
/// primitive behind "how much of this range falls on each day," reused by
/// <see cref="PlannedFocusHoursCalculator"/>, <see cref="Entities.NenContract.AllowsBlockPlacement"/>'s
/// per-day Work Hours check, and Split's day-membership validation. A pure, static, Kind-agnostic
/// function — it treats whatever DateTimes it's given as truth, so callers are responsible for
/// passing already-local-converted values wherever "calendar day" is meant to be the user's local day.
/// </summary>
public static class DayRangeSplitter
{
    public static IEnumerable<(DateOnly Date, DateTime SegmentStart, DateTime SegmentEnd)> Split(DateTime start, DateTime end)
    {
        if (end <= start) yield break;

        var cursor = start;
        while (cursor < end)
        {
            var nextDayStart = cursor.Date.AddDays(1);
            var segmentEnd = end < nextDayStart ? end : nextDayStart;

            yield return (DateOnly.FromDateTime(cursor.Date), cursor, segmentEnd);

            cursor = segmentEnd;
        }
    }
}
