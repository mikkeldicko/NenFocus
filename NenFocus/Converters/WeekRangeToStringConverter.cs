using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

public sealed class WeekRangeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime weekStart)
        {
            var end = weekStart.AddDays(6);
            return weekStart.Month == end.Month
                ? $"{weekStart:MMM d} â€“ {end.Day}, {end.Year}"
                : $"{weekStart:MMM d} â€“ {end:MMM d}, {end.Year}";
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

