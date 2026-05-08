using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NenFocus.ViewModels;

/// <summary>
/// Converts a bool (is this dot's step active?) to a fill color.
/// Active → gold, inactive → light gray.
/// </summary>
public sealed class BoolToDotFillConverter : IValueConverter
{
    public static readonly BoolToDotFillConverter Instance = new();

    private static readonly IBrush ActiveBrush   = SolidColorBrush.Parse("#B8922A");
    private static readonly IBrush InactiveBrush = SolidColorBrush.Parse("#DDDDE8");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
