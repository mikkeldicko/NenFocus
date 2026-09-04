using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>Same compact "1h 30m"/"30m" format as DurationToStringConverter, for a raw double-minutes value (e.g. RedemptionEntry.MinutesCovered) rather than a TimeSpan.</summary>
public sealed class MinutesToStringConverter : IValueConverter
{
    public static readonly MinutesToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double minutes ? FormatMinutes(minutes) : string.Empty;

    /// <summary>Plain static entry point for non-XAML callers (e.g. a ViewModel string
    /// property) that need the exact same "1h 30m"/"30m" format without going through
    /// IValueConverter — one formatting rule, reusable from either side.</summary>
    public static string FormatMinutes(double minutes)
    {
        var total = Math.Max(0, (int)Math.Round(minutes));
        var hours = total / 60;
        var mins = total % 60;

        return hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
