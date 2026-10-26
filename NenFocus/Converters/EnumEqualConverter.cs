using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// Returns true if the binding value's ToString() equals the converter parameter string.
/// Works for any enum, including a nullable one (value is null when unset). Used to drive
/// badge visibility off BlockContractBadge without a dedicated converter per enum.
/// </summary>
public sealed class EnumEqualConverter : IValueConverter
{
    public static readonly EnumEqualConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter as string;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
