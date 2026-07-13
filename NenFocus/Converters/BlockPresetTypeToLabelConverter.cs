using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class BlockPresetTypeToLabelConverter : IValueConverter
{
    public static readonly BlockPresetTypeToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BlockPresetType.Background ? "Background" : "Focus";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
