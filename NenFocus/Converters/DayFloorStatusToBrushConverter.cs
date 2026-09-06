using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;

namespace NenFocus.Converters;

/// <summary>
/// Background color for a day-header floor badge. A Scheduled (Upcoming) contract's day
/// always renders as a uniform passive grey, regardless of met/unmet/rest — none of that
/// is actionable until the contract actually starts, so grey is reserved exclusively for
/// "not yet relevant." An Active contract's rest day instead uses its own teal family,
/// graduated by Timing exactly like a real floor day's met/unmet coloring is (past/today/
/// future each get their own shade) — deliberately a hue nothing else in this converter
/// uses, so a rest day never reads as "met" (green), "future on-track" (blue), or
/// "upcoming contract" (grey); it's its own distinct, calm category. Otherwise, today and
/// past are both "resolved" (checked against completed hours) but rendered distinctly:
/// today-unmet is amber ("still in progress," the day isn't over — not a real miss yet),
/// past-unmet is dark red (a real miss). A sealed contract's future day (ShowSimplifiedFuture
/// — every strictness rule, not just Daily Flexibility: Seal/Edit's own invariant plus the
/// post-seal floor-protection guards make it provably always safe by the time it's shown)
/// always renders met — the (Future, false) branch below is only ever reached during live
/// drafting, where ContractStrictnessRule/ContractStatus are both null and a future day
/// genuinely can be under-floor as feedback to fix before sealing (drafting's own rest days
/// keep the plain grey — there is no real Active/Scheduled contract yet to color them by).
/// A ContractFloor contract's Past days keep the ordinary met/unmet coloring (green/dark
/// red) — only their label format differs, see DayFloorPreviewViewModel. Pairs with
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
    private static readonly IBrush FutureRestBrush = SolidColorBrush.Parse("#C5E4DE");
    private static readonly IBrush TodayRestBrush = SolidColorBrush.Parse("#4FA89C");
    private static readonly IBrush PastRestBrush = SolidColorBrush.Parse("#2E7A70");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DayFloorPreviewViewModel status) return null;
        if (status.ContractStatus == NenContractStatus.Scheduled) return RestBrush;
        if (status.IsRestDay)
        {
            if (status.ContractStatus != NenContractStatus.Active) return RestBrush; // drafting
            return status.Timing switch
            {
                DayTiming.Past => PastRestBrush,
                DayTiming.Today => TodayRestBrush,
                _ => FutureRestBrush
            };
        }
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
