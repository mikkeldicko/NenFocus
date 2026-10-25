using System;
using System.Collections.Generic;
using System.Linq;

namespace NenFocus.Views.Controls;

/// <summary>
/// Windows an ordered list down to at most <paramref name="windowSize"/> items centered on
/// <paramref name="centerIndex"/>, padding toward whichever side has room when centering
/// would run past an edge — e.g. windowing 5 around the second-to-last item pulls one more
/// from before it rather than showing only 4. Pure and list-shape-agnostic — TimeOfDayBox's
/// dropdown, for a Start field's list and an End field's alike (Gap 89 Slice B).
/// </summary>
internal static class DropdownWindow
{
    /// <summary>
    /// Pure index arithmetic behind Center&lt;T&gt; — split out so TimeOfDayBox can compute the
    /// same 5-item window bounds to scroll its popup to, from just the item count.
    /// </summary>
    internal static (int Start, int End) Bounds(int centerIndex, int count, int windowSize)
    {
        if (count <= windowSize) return (0, Math.Max(0, count - 1));

        var half = windowSize / 2;
        var start = centerIndex - half;
        var end = centerIndex + (windowSize - 1 - half);

        if (start < 0)
        {
            end += -start;
            start = 0;
        }
        if (end > count - 1)
        {
            start -= end - (count - 1);
            end = count - 1;
        }
        start = Math.Max(0, start);

        return (start, end);
    }

    internal static IReadOnlyList<T> Center<T>(IReadOnlyList<T> items, int centerIndex, int windowSize)
    {
        if (items.Count <= windowSize) return items;

        var (start, end) = Bounds(centerIndex, items.Count, windowSize);
        return items.Skip(start).Take(end - start + 1).ToList();
    }
}
