using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class BlockProfileTypeToLabelConverter : IValueConverter
{
    public static readonly BlockProfileTypeToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BlockType.Background ? "BACKGROUND" : "FOCUS";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
