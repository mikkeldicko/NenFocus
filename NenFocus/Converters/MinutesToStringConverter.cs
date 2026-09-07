using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>Compact "1h 30m"/"1h"/"30 min" format for a raw double-minutes value (e.g.
/// RedemptionEntry.MinutesCovered) — mirrors DurationToStringConverter's shape, adjusted so
/// a whole-hour value never shows a pointless "0m" and a sub-hour value spells out "min"
/// (only "h"+"m" combine into the abbreviated form when both are non-zero).</summary>
public sealed class MinutesToStringConverter : IValueConverter
{
    public static readonly MinutesToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            double minutes => FormatMinutes(minutes),
            int minutes => FormatMinutes(minutes),
            _ => string.Empty
        };

    /// <summary>Plain static entry point for non-XAML callers (e.g. a ViewModel string
    /// property) that need the exact same format without going through IValueConverter —
    /// one formatting rule, reusable from either side.</summary>
    public static string FormatMinutes(double minutes)
    {
        var total = Math.Max(0, (int)Math.Round(minutes));
        var hours = total / 60;
        var mins = total % 60;

        if (hours > 0 && mins > 0) return $"{hours}h {mins}m";
        if (hours > 0) return $"{hours}h";
        return $"{mins} min";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
