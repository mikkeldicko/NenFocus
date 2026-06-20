using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.ViewModels;

public sealed class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToString(parameter as string ?? "h:mm tt", culture);
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
