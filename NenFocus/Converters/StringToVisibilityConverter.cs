using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

public sealed class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter IsNonEmpty = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
