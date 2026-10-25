using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>"Mar 5, Mar 12" for a list of calendar days — the same "MMM d" the rules step's
/// rest-day chips show — or "None" when there are none (Gap 89 Slice B: the wizard summary).</summary>
public sealed class DateListToLabelConverter : IValueConverter
{
    public static readonly DateListToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable<DateOnly> dates && dates.Any()
            ? string.Join(", ", dates.Select(d => d.ToString("MMM d", culture)))
            : "None";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
