using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NenFocus.Converters;

/// <summary>
/// Converts a day floor preview's IsMet bool to a background color for its chip
/// on the Planner's drafting action bar. Met (or rest day) → green, short → amber.
/// </summary>
public sealed class BoolToFloorStatusBrushConverter : IValueConverter
{
    public static readonly BoolToFloorStatusBrushConverter Instance = new();

    private static readonly IBrush MetBrush = SolidColorBrush.Parse("#DDF0DD");
    private static readonly IBrush ShortBrush = SolidColorBrush.Parse("#F5DCC0");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? MetBrush : ShortBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
