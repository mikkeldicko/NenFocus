using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class BlockPresetTypeToForegroundConverter : IValueConverter
{
    public static readonly BlockPresetTypeToForegroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BlockPresetType.Background
            ? BlockPresetTypeToColorConverter.BackgroundForeground
            : BlockPresetTypeToColorConverter.FocusForeground;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
