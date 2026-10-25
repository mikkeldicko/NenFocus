using System;

namespace NenFocus.ViewModels.Planner;

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
    /// Clamps a duration-preserving move's snapped start so the tile's exact original
    /// duration always fits within [firstHour:00, 24:00] — shifting start earlier if needed
    /// rather than ever truncating duration. A drag-MOVE only relocates a tile; it must
    /// never resize it, even when the original duration isn't a clean 15-minute multiple
    /// (e.g. 38 minutes) — the caller derives duration from the tile's own unsnapped
    /// Start/End, never re-snaps it here.
    /// </summary>
    internal static TimeSpan ClampMoveStart(TimeSpan snappedStart, TimeSpan duration, int firstHour)
    {
        var minTime = TimeSpan.FromHours(firstHour);
        var maxTime = TimeSpan.FromHours(24);

        if (snappedStart < minTime) snappedStart = minTime;
        if (snappedStart + duration > maxTime) snappedStart = maxTime - duration;
        if (snappedStart < minTime) snappedStart = minTime; // duration longer than the whole visible range — best effort

        return snappedStart;
    }

    /// <summary>
    /// Clamps a top-edge resize's snapped candidate start against the FIXED, unsnapped
    /// anchorEnd — the bottom edge the user isn't touching. Ensures at least a 15-minute
    /// result without ever moving anchorEnd itself.
    /// </summary>
    internal static TimeSpan ClampResizeTopStart(TimeSpan candidateStart, TimeSpan anchorEnd, int firstHour)
    {
        var minTime = TimeSpan.FromHours(firstHour);
        var minDuration = TimeSpan.FromMinutes(SnapMinutes);

        if (candidateStart < minTime) candidateStart = minTime;
        if (candidateStart > anchorEnd - minDuration) candidateStart = anchorEnd - minDuration;

        return candidateStart;
    }

    /// <summary>
    /// Clamps a bottom-edge resize's snapped candidate end against the FIXED, unsnapped
    /// anchorStart — the top edge the user isn't touching. Ensures at least a 15-minute
    /// result without ever moving anchorStart itself.
    /// </summary>
    internal static TimeSpan ClampResizeBottomEnd(TimeSpan candidateEnd, TimeSpan anchorStart, int firstHour)
    {
        var maxTime = TimeSpan.FromHours(24);
        var minDuration = TimeSpan.FromMinutes(SnapMinutes);

        if (candidateEnd > maxTime) candidateEnd = maxTime;
        if (candidateEnd < anchorStart + minDuration) candidateEnd = anchorStart + minDuration;

        return candidateEnd;
    }

    private static int SnapToQuarterHour(int totalMinutes)
    {
        int remainder = totalMinutes % SnapMinutes;
        return remainder >= SnapMinutes / 2
            ? totalMinutes + (SnapMinutes - remainder)
            : totalMinutes - remainder;
    }
}
