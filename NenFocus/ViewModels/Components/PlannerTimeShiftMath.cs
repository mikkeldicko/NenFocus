using System;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Pure Google-Calendar-style duration-preserving math for the Planner block form's
/// Start/End text fields — zero business-rule content, same category as
/// <see cref="PlannerLayout"/>/<see cref="PlannerDragMath"/>. Editing Start shifts End to
/// preserve the current duration; editing End re-derives the duration Start-edits will
/// preserve going forward. Form-field-only — drag/resize on the calendar grid is a
/// separate code path and is untouched by this.
///
/// End may resolve to the day after Start (never further) — a typed End at or before
/// Start rolls forward exactly one day, Google-style, never more.
/// </summary>
internal static class PlannerTimeShiftMath
{
    private static readonly TimeSpan OneDay = TimeSpan.FromHours(24);

    /// <summary>True when an End time-of-day, given a Start time-of-day, resolves to the day after Start.</summary>
    internal static bool EndRollsToNextDay(TimeSpan startTimeOfDay, TimeSpan endTimeOfDay)
        => endTimeOfDay <= startTimeOfDay;

    /// <summary>
    /// The duration implied by a Start/End time-of-day pair, resolving End to the next day
    /// when it's at or before Start. Always in (0h, 24h] — used when End is edited, to
    /// compute what duration the next Start edit should preserve.
    /// </summary>
    internal static TimeSpan ResolvedDuration(TimeSpan startTimeOfDay, TimeSpan endTimeOfDay)
    {
        var end = EndRollsToNextDay(startTimeOfDay, endTimeOfDay) ? endTimeOfDay + OneDay : endTimeOfDay;
        return end - startTimeOfDay;
    }

    /// <summary>
    /// The new End time-of-day that preserves <paramref name="duration"/> from
    /// <paramref name="newStartTimeOfDay"/> — used when Start is edited. Whether the
    /// result rolls to the next day is derivable separately via <see cref="EndRollsToNextDay"/>.
    /// </summary>
    internal static TimeSpan ShiftEnd(TimeSpan newStartTimeOfDay, TimeSpan duration)
    {
        var raw = newStartTimeOfDay + duration;
        return raw >= OneDay ? raw - OneDay : raw;
    }
}
