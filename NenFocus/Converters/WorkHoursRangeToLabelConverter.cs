using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>"09:00 – 17:00" for a set Work Hours window, "No constraint" when none is fixed — a
/// MultiBinding of start and end (Gap 89 Slice B). One wording for the preset cards, the wizard's
/// rules step and its summary, which used to build it three times.</summary>
public sealed class WorkHoursRangeToLabelConverter : IMultiValueConverter
{
    public static readonly WorkHoursRangeToLabelConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values is [TimeOnly start, TimeOnly end, ..] ? $"{start:HH:mm} – {end:HH:mm}" : "No constraint";

    /// <summary>Bare "9:00 AM – 5:00 PM", no fallback — only for the drafting placement message
    /// quoting the window it rejected against, which Gap 68 deletes with ValidateDraftPlacement.</summary>
    public static string FormatKnownWindow12Hour(TimeOnly start, TimeOnly end) => $"{start:h:mm tt} – {end:h:mm tt}";
}
