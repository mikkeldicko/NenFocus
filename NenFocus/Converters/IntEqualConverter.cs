using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// Returns true if the int binding value equals the string converter parameter.
/// Used to highlight the active hour/minute chip in HatsuPageView.
/// </summary>
public sealed class IntEqualConverter : IValueConverter
{
    public static readonly IntEqualConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && parameter is string s && int.TryParse(s, out var p))
            return i == p;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
