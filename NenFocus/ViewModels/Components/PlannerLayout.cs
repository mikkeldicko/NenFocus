using System;

namespace NenFocus.ViewModels.Components;

internal static class PlannerLayout
{
    /// <summary>
    /// Floor for a tile's rendered height, in pixels — every block is at least this
    /// tall regardless of scheduled duration, so a 1-5 minute block still shows as a
    /// visible, clickable strip (same idea as Google Calendar's thin short-event bars).
    /// </summary>
    internal const double MinimumTileHeightPx = 32.0;

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
        var height = Math.Max(MinimumTileHeightPx, (durationMinutes / 60.0) * hourRowHeight);
        return (top, height);
    }

    internal static double GetCurrentTimeY(DateTime localNow, int firstHour, double hourRowHeight)
    {
        double totalMinutes = (localNow.Hour - firstHour) * 60 + localNow.Minute;
        return (totalMinutes / 60.0) * hourRowHeight;
    }
}
