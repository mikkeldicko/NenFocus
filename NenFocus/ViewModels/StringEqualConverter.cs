using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.ViewModels;

/// <summary>
/// Returns true if the binding value equals the converter parameter string.
/// Used to highlight the active chip option in HatsuPageView.
/// </summary>
public sealed class StringEqualConverter : IValueConverter
{
    public static readonly StringEqualConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p && s == p;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
