using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

/// <summary>Mirrors the Planner tile's missedTile/lateOrEarlyTile/completedOnTimeTile colors (UI_UX_STANDARDS.md).</summary>
public sealed class FocusBlockOutcomeToBrushConverter : IValueConverter
{
    public static readonly FocusBlockOutcomeToBrushConverter Instance = new();

    private static readonly IBrush Missed = SolidColorBrush.Parse("#C0392B");
    private static readonly IBrush LateOrEarly = SolidColorBrush.Parse("#D97706");
    private static readonly IBrush CompletedOnTime = SolidColorBrush.Parse("#3A9E6A");
    private static readonly IBrush Neutral = SolidColorBrush.Parse("#888899");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FocusBlockOutcome.Missed => Missed,
            FocusBlockOutcome.StartedLate => LateOrEarly,
            FocusBlockOutcome.EndedEarly => LateOrEarly,
            FocusBlockOutcome.CompletedOnTime => CompletedOnTime,
            _ => Neutral
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
