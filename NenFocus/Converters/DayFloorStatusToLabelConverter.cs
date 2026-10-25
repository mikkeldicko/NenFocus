using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Converters;

/// <summary>
/// A day's floor badge text — "Rest", "2.5h", or "2h / 3h" when the comparison is the point (a
/// draft, or today against its floor). Bound to the whole DayFloorStatus like its brush and
/// foreground siblings, so no contract on the day reads as nothing. Which numbers to show is the
/// status's own (ShowSimplifiedFuture, IsContractFloorMode); this only words them (Gap 89 Slice B).
/// </summary>
public sealed class DayFloorStatusToLabelConverter : IValueConverter
{
    public static readonly DayFloorStatusToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DayFloorPreviewViewModel day) return string.Empty;
        if (day.IsRestDay) return "Rest";

        var current = HoursToStringConverter.FormatHours(day.CurrentHours);
        var currentOnly = (day.IsContractFloorMode && day.Timing == DayTiming.Past) || day.ShowSimplifiedFuture;
        return !currentOnly && day.RequiredFloorHours is { } required
            ? $"{current} / {HoursToStringConverter.FormatHours(required)}"
            : current;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
