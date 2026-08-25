using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// Formats a duration as a parenthesized End-dropdown suffix — "(30 mins)", "(1 hr)",
/// "(1.5 hrs)", "(3 hrs)" — Google's own decimal-hours-past-the-hour-mark wording style.
/// Sibling to <see cref="DurationToStringConverter"/>, not a modification of it: that
/// converter is shared with the post-session completion screen and the block snapshot
/// modal, and changing its output would silently change both.
/// </summary>
public sealed class DurationLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan ts) return string.Empty;

        if (ts.TotalMinutes < 60)
            return $"({(int)ts.TotalMinutes} mins)";

        var hours = ts.TotalHours;
        if (hours == 1)
            return "(1 hr)";

        var hoursText = hours % 1 == 0 ? hours.ToString("0", culture) : hours.ToString("0.#", culture);
        return $"({hoursText} hrs)";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
