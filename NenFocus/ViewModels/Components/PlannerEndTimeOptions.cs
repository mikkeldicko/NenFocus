using System;
using System.Collections.Generic;
using NenFocus.Domain.Entities;

namespace NenFocus.ViewModels.Components;

/// <summary>One selectable entry in the Planner form's End dropdown — TimeText is the
/// raw parseable value written back to FormEndTime on selection (via ValueMemberBinding),
/// Duration drives the parenthesized suffix shown for each entry (via DurationLabelConverter).</summary>
public sealed record PlannerEndTimeOption(string TimeText, TimeSpan Duration);

/// <summary>
/// Generates the End dropdown's duration offsets from Start — Start+15/30/45/60 (first
/// hour, 15-minute steps), then +90/+120/+150… (30-minute steps from there on), wrapping
/// past midnight back to just before Start, truncated at the earlier of that wraparound
/// or the 12-hour Focus cap (Focus blocks only — Background gets no cap truncation).
/// Pure and Start-independent — same offset sequence regardless of Start's own minute
/// alignment, since these are durations, not clock times; the caller resolves each into
/// an actual End time-of-day via <see cref="PlannerTimeShiftMath.ShiftEnd"/>.
/// </summary>
internal static class PlannerEndTimeOptions
{
    /// <summary>Latest offset that stays within the same day-cycle — 23:30, one 30-minute
    /// step short of a full 24h (which would place End back exactly on Start).</summary>
    private static readonly TimeSpan JustUnderOneDay = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(30);

    /// <param name="maximum">The longest the block may be, offered itself when it isn't a step —
    /// a Cover Deficit tile stops at the debt (Gap 106, decision 19).</param>
    internal static IReadOnlyList<TimeSpan> GenerateOffsets(bool isFocus, TimeSpan? maximum = null)
    {
        var maxDuration = isFocus ? TimeSpan.FromHours(FocusBlock.MaxDurationHours) : JustUnderOneDay;
        if (maximum is { } cap && cap < maxDuration) maxDuration = cap;
        var offsets = new List<TimeSpan>();

        for (var minutes = 15; minutes <= 60; minutes += 15)
            offsets.Add(TimeSpan.FromMinutes(minutes));
        for (var minutes = 90; minutes <= JustUnderOneDay.TotalMinutes; minutes += 30)
            offsets.Add(TimeSpan.FromMinutes(minutes));

        offsets.RemoveAll(offset => offset > maxDuration);
        if (maximum is not null && (offsets.Count == 0 || offsets[^1] != maxDuration))
            offsets.Add(maxDuration);

        return offsets;
    }
}
