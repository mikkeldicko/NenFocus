using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Converters;

/// <summary>A day's Bonus pill text, "+1.5h bonus" — bound to the whole DayFloorStatus, like
/// DayFloorStatusHasBonusConverter which decides whether the pill shows (Gap 89 Slice B).</summary>
public sealed class DayFloorStatusToBonusLabelConverter : IValueConverter
{
    public static readonly DayFloorStatusToBonusLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DayFloorPreviewViewModel day ? $"+{HoursToStringConverter.FormatHours(day.BonusHours)} bonus" : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
