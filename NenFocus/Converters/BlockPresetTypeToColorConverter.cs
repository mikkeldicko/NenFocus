using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class BlockPresetTypeToColorConverter : IValueConverter
{
    public static readonly BlockPresetTypeToColorConverter Instance = new();

    public static readonly IBrush FocusForeground      = SolidColorBrush.Parse("#8B7320");
    public static readonly IBrush BackgroundForeground = SolidColorBrush.Parse("#4A5568");

    private static readonly IBrush FocusBackground      = SolidColorBrush.Parse("#F5EDD8");
    private static readonly IBrush BackgroundBackground = SolidColorBrush.Parse("#E8ECF0");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BlockPresetType.Background ? BackgroundBackground : FocusBackground;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
