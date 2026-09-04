using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.ViewModels.Components;

namespace NenFocus.Converters;

/// <summary>
/// Whether the Bonus pill should show for this day — bound to the whole DayFloorStatus
/// object, not its .HasBonus sub-property, so a null DayFloorStatus (no contract covers
/// this day) resolves to false instead of leaving IsVisible at its uninitialized default
/// (true) the way a null sub-property binding does. Pairs with DayFloorStatusToBrushConverter/
/// DayFloorStatusToForegroundConverter, same null-safety pattern.
/// </summary>
public sealed class DayFloorStatusHasBonusConverter : IValueConverter
{
    public static readonly DayFloorStatusHasBonusConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DayFloorPreviewViewModel status && status.HasBonus;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
