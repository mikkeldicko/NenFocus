using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// Converts a collection count to visibility.
/// Used with ConverterParameter to check for zero or non-zero.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter IsZero = new(checkZero: true);
    public static readonly CountToVisibilityConverter IsNonZero = new(checkZero: false);

    private readonly bool _checkZero;

    private CountToVisibilityConverter(bool checkZero) => _checkZero = checkZero;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        return _checkZero ? count == 0 : count > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

