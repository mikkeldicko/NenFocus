using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;

namespace NenFocus.Converters;

/// <summary>
/// Foreground for a day-header floor badge, paired with DayFloorStatusToBrushConverter.
/// White on every saturated background (today/past verdict colors, an Active contract's
/// mid/deep-teal today/past rest day, and a future "met" badge's medium blue — including
/// ShowSimplifiedFuture, always met by construction); dark #1A1A2E on the light grey (a
/// Scheduled/Upcoming contract's day, or a drafting rest day), the pale future-unmet pink,
/// and an Active contract's pale-teal future rest day — matching UI_UX_STANDARDS.md's
/// established convention of dark text on light backgrounds.
/// </summary>
public sealed class DayFloorStatusToForegroundConverter : IValueConverter
{
    public static readonly DayFloorStatusToForegroundConverter Instance = new();

    private static readonly IBrush DarkText = SolidColorBrush.Parse("#1A1A2E");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DayFloorPreviewViewModel status) return null;
        if (status.ContractStatus == NenContractStatus.Scheduled) return DarkText;
        if (status.IsRestDay)
        {
            if (status.ContractStatus != NenContractStatus.Active) return DarkText; // drafting, light grey
            return status.Timing == DayTiming.Future ? DarkText : Brushes.White; // pale-teal future vs. mid/deep-teal today/past
        }

        var isSaturatedBackground = status.Timing is DayTiming.Today or DayTiming.Past || status.IsMet;
        return isSaturatedBackground ? Brushes.White : DarkText;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
