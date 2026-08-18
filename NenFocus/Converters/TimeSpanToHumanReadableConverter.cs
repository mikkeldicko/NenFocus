using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// Spells out a TimeSpan in words ("5 minutes", "1 hour", "1h 30m") for prose contexts
/// like the Ritual's late-to-block notice — distinct from DurationToStringConverter's
/// compact "5m"/"1h 30m" form, which reads fine in a stat chip but not mid-sentence.
/// </summary>
public sealed class TimeSpanToHumanReadableConverter : IValueConverter
{
    public static readonly TimeSpanToHumanReadableConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan ts) return string.Empty;

        var totalMinutes = Math.Max(0, (int)ts.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours == 0)
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";

        if (minutes == 0)
            return hours == 1 ? "1 hour" : $"{hours} hours";

        return $"{hours}h {minutes}m";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
