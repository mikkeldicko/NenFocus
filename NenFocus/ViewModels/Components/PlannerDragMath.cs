using System;
using System.Collections.Generic;
using System.Linq;

namespace NenFocus.ViewModels.Components;

internal static class PlannerDragMath
{
    private const int SnapMinutes = 15;

    /// <summary>
    /// Converts a Y offset within the canvas to a snapped local time-of-day.
    /// Result is clamped to [firstHour:00, 24:00].
    /// </summary>
    internal static TimeSpan YToSnappedTime(double y, int firstHour, double hourRowHeight)
    {
        double totalMinutes = (y / hourRowHeight) * 60.0 + firstHour * 60;
        int snapped = SnapToQuarterHour((int)Math.Round(totalMinutes));
        snapped = Math.Clamp(snapped, firstHour * 60, 24 * 60);
        return TimeSpan.FromMinutes(snapped);
    }

    /// <summary>
    /// Converts a pixel delta (PointerMoved Y delta from drag start) to a snapped minute offset.
    /// May be negative (drag upward).
    /// </summary>
    internal static int DeltaYToSnappedMinutes(double deltaY, double hourRowHeight)
    {
        double rawMinutes = (deltaY / hourRowHeight) * 60.0;
        int raw = (int)Math.Round(rawMinutes);
        int sign = raw < 0 ? -1 : 1;
        int abs = Math.Abs(raw);
        int snapped = (abs / SnapMinutes) * SnapMinutes;
        if (abs % SnapMinutes >= SnapMinutes / 2) snapped += SnapMinutes;
        return sign * snapped;
    }

    /// <summary>
    /// Clamps a time range so start ≥ firstHour:00 and end ≤ 24:00, with a minimum duration of 15 minutes.
    /// </summary>
    internal static (TimeSpan Start, TimeSpan End) ClampRange(TimeSpan start, TimeSpan end, int firstHour)
    {
        var minTime = TimeSpan.FromHours(firstHour);
        var maxTime = TimeSpan.FromHours(24);
        var minDuration = TimeSpan.FromMinutes(SnapMinutes);

        start = start < minTime ? minTime : start;
        end = end > maxTime ? maxTime : end;

        if (end - start < minDuration)
            end = start + minDuration;
        if (end > maxTime)
        {
            end = maxTime;
            start = end - minDuration;
        }

        return (start, end);
    }

    /// <summary>
    /// Pre-flight UX collision check. Returns true if the candidate Focus block would overlap
    /// any other Focus tile on the same day column. Uses half-open interval [start, end) so
    /// touching edges are not counted as overlap.
    /// Background blocks never cause a collision.
    /// </summary>
    internal static bool WouldCollide(
        Guid movingBlockId,
        bool candidateIsFocus,
        DateTime candidateStartUtc,
        DateTime candidateEndUtc,
        IEnumerable<PlannedBlockTileViewModel> dayTiles)
    {
        if (!candidateIsFocus) return false;

        return dayTiles.Any(t =>
            t.Id != movingBlockId &&
            t.IsFocus &&
            candidateStartUtc < t.End.ToUniversalTime() &&
            candidateEndUtc > t.Start.ToUniversalTime());
    }

    private static int SnapToQuarterHour(int totalMinutes)
    {
        int remainder = totalMinutes % SnapMinutes;
        return remainder >= SnapMinutes / 2
            ? totalMinutes + (SnapMinutes - remainder)
            : totalMinutes - remainder;
    }
}
