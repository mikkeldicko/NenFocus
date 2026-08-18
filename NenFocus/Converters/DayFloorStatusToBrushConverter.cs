using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;

namespace NenFocus.Converters;

/// <summary>
/// Background color for a day-header floor badge. Rest day -> neutral gray. Otherwise,
/// today and past are both "resolved" (checked against completed hours) but rendered
/// distinctly: today-unmet is amber ("still in progress," the day isn't over — not a
/// real miss yet), past-unmet is dark red (a real miss). Future days ("planned," not
/// yet resolved) use lighter tints. Pairs with DayFloorStatusToForegroundConverter.
/// </summary>
public sealed class DayFloorStatusToBrushConverter : IValueConverter
{
    public static readonly DayFloorStatusToBrushConverter Instance = new();

    private static readonly IBrush RestBrush = SolidColorBrush.Parse("#DDDDE8");
    private static readonly IBrush MetBrush = SolidColorBrush.Parse("#3A9E6A");
    private static readonly IBrush TodayUnmetBrush = SolidColorBrush.Parse("#E8A93A");
    private static readonly IBrush PastUnmetBrush = SolidColorBrush.Parse("#C0392B");
    private static readonly IBrush FutureMetBrush = SolidColorBrush.Parse("#7BA7C9");
    private static readonly IBrush FutureUnmetBrush = SolidColorBrush.Parse("#F0B8B0");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DayFloorPreviewViewModel status) return null;
        if (status.IsRestDay) return RestBrush;

        return (status.Timing, status.IsMet) switch
        {
            (DayTiming.Today, true) => MetBrush,
            (DayTiming.Today, false) => TodayUnmetBrush,
            (DayTiming.Past, true) => MetBrush,
            (DayTiming.Past, false) => PastUnmetBrush,
            (DayTiming.Future, true) => FutureMetBrush,
            (DayTiming.Future, false) => FutureUnmetBrush,
            _ => RestBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
