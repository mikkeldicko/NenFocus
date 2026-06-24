using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class BlockProfileTypeToColorConverter : IValueConverter
{
    public static readonly BlockProfileTypeToColorConverter Instance = new();

    private static readonly IBrush FocusBrush      = SolidColorBrush.Parse("#1E3A8A");
    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#166534");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BlockType.Background ? BackgroundBrush : FocusBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
