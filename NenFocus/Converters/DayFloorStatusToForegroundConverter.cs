using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;

namespace NenFocus.Converters;

/// <summary>
/// Foreground for a day-header floor badge, paired with DayFloorStatusToBrushConverter.
/// White on the saturated today/past verdict colors (dark green, amber, dark red);
/// dark #1A1A2E on the light rest/future tints — matches UI_UX_STANDARDS.md's
/// established convention of dark text on light backgrounds throughout the app.
/// </summary>
public sealed class DayFloorStatusToForegroundConverter : IValueConverter
{
    public static readonly DayFloorStatusToForegroundConverter Instance = new();

    private static readonly IBrush DarkText = SolidColorBrush.Parse("#1A1A2E");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DayFloorPreviewViewModel status) return null;
        if (status.IsRestDay) return DarkText;
        return status.Timing is DayTiming.Today or DayTiming.Past ? Brushes.White : DarkText;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
