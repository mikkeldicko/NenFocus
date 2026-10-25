using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>"Jan 1, 2026 – Jan 31, 2026" for a DateOnly range — a MultiBinding of start and end
/// (Gap 89 Slice B: ViewModels hand the dates over, the View words them).</summary>
public sealed class DateRangeToLabelConverter : IMultiValueConverter
{
    public static readonly DateRangeToLabelConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values is [DateOnly start, DateOnly end, ..] ? $"{start:MMM d, yyyy} – {end:MMM d, yyyy}" : string.Empty;

    /// <summary>"Jan 1 – Jan 31" — only for the drafting placement messages, which Gap 68 deletes
    /// with ValidateDraftPlacement.</summary>
    public static string FormatRangeShort(DateOnly start, DateOnly end) => $"{start:MMM d} – {end:MMM d}";
}
