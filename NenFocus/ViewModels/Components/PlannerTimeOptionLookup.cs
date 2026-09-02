using System;
using System.Collections.Generic;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Pure lookup math for the Planner block form's Google-Calendar-style time dropdown: the
/// list of options is always shown in full (never sliced down by typing — see
/// PlannerPageViewModel's Visible*TimeOptions removal), so typing and arrow-key navigation
/// only ever change *where the popup is scrolled/highlighted*, never *what's in it*.
/// Zero business-rule content, same category as <see cref="PlannerTimeShiftMath"/>.
/// </summary>
internal static class PlannerTimeOptionLookup
{
    /// <summary>
    /// Best-guess time-of-day from partial typed digits, for live dropdown centering only —
    /// not a validity check (see PlannerPageViewModel.TryParseFormTime for that). Strips any
    /// non-digit characters and reads the leading one-or-two digits as an hour: "14" and
    /// "144" both guess hour 14 (a third digit doesn't refine the guess until the text is a
    /// complete, valid time — see the two-digit-then-fall-back-to-one-digit rule below).
    /// "99" falls back to a single-digit guess (hour 9) since 99 isn't a valid hour.
    /// Returns false when there are no digits to guess from at all.
    /// </summary>
    internal static bool TryGuessHour(string text, out TimeSpan guess)
    {
        Span<char> digits = stackalloc char[2];
        var digitCount = 0;
        foreach (var c in text)
        {
            if (!char.IsDigit(c)) continue;
            if (digitCount < 2) digits[digitCount] = c;
            digitCount++;
            if (digitCount >= 2) break;
        }

        if (digitCount == 0)
        {
            guess = default;
            return false;
        }

        if (digitCount >= 2)
        {
            var twoDigitHour = (digits[0] - '0') * 10 + (digits[1] - '0');
            if (twoDigitHour <= 23)
            {
                guess = TimeSpan.FromHours(twoDigitHour);
                return true;
            }
        }

        guess = TimeSpan.FromHours(digits[0] - '0');
        return true;
    }

    /// <summary>Index of the option in <paramref name="options"/> closest to <paramref name="target"/>, for live centering as the user types.</summary>
    internal static int NearestIndex(TimeSpan target, IReadOnlyList<TimeSpan> options)
    {
        var bestIndex = 0;
        var bestDiff = TimeSpan.MaxValue;
        for (var i = 0; i < options.Count; i++)
        {
            var diff = (options[i] - target).Duration();
            if (diff >= bestDiff) continue;
            bestDiff = diff;
            bestIndex = i;
        }
        return bestIndex;
    }

    /// <summary>
    /// Index of the first option strictly after <paramref name="current"/> — used for every
    /// Down-arrow press. Composes the "snap to nearest slot" and "step to the next slot"
    /// behaviors into one rule: pressing Down from an off-grid value (14:07) lands on the
    /// next slot forward (14:15), and pressing Down again from an on-grid value just steps
    /// to the following one — both are "first option strictly after current," no special
    /// case needed for either. Clamped at the last index (Down does nothing once at the end).
    /// </summary>
    internal static int IndexAfter(TimeSpan current, IReadOnlyList<TimeSpan> options)
    {
        for (var i = 0; i < options.Count; i++)
            if (options[i] > current) return i;
        return options.Count - 1;
    }

    /// <summary>Mirror of <see cref="IndexAfter"/> for Up-arrow: the last option strictly before <paramref name="current"/>, clamped at index 0.</summary>
    internal static int IndexBefore(TimeSpan current, IReadOnlyList<TimeSpan> options)
    {
        for (var i = options.Count - 1; i >= 0; i--)
            if (options[i] < current) return i;
        return 0;
    }
}
