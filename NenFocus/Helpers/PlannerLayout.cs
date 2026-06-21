using System;

namespace NenFocus.Helpers;

internal static class PlannerLayout
{
    internal static (double Top, double Height)? GetTilePosition(
        DateTime localStart,
        DateTime localEnd,
        DateTime visibleStart,
        DateTime visibleEnd,
        double hourRowHeight)
    {
        var clippedStart = localStart < visibleStart ? visibleStart : localStart;
        var clippedEnd   = localEnd   > visibleEnd   ? visibleEnd   : localEnd;
        if (clippedEnd <= clippedStart) return null;

        var startMinutes    = (clippedStart - visibleStart).TotalMinutes;
        var durationMinutes = (clippedEnd   - clippedStart).TotalMinutes;
        var top    = (startMinutes    / 60.0) * hourRowHeight;
        var height = Math.Max(hourRowHeight * 0.5, (durationMinutes / 60.0) * hourRowHeight);
        return (top, height);
    }

    internal static double GetCurrentTimeY(DateTime localNow, int firstHour, double hourRowHeight)
    {
        double totalMinutes = (localNow.Hour - firstHour) * 60 + localNow.Minute;
        return (totalMinutes / 60.0) * hourRowHeight;
    }
}
