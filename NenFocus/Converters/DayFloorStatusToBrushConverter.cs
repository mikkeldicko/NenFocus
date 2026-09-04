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
/// real miss yet), past-unmet is dark red (a real miss). A sealed contract's future day
/// (ShowSimplifiedFuture — every strictness rule, not just Daily Flexibility: Seal/Edit's
/// own invariant plus the post-seal floor-protection guards make it provably always safe
/// by the time it's shown) always renders met — the (Future, false) branch below is only
/// ever reached during live drafting, where ContractStrictnessRule is null and a future
/// day genuinely can be under-floor as feedback to fix before sealing. A ContractFloor
/// contract's Past days keep the ordinary met/unmet coloring (green/dark red) — only their
/// label format differs, see DayFloorPreviewViewModel. Pairs with
/// DayFloorStatusToForegroundConverter.
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
        if (status.ShowSimplifiedFuture) return FutureMetBrush;

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
