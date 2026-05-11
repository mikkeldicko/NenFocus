using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.ViewModels;

public sealed class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.ToString(parameter as string ?? @"hh\:mm\:ss");
        return "00:00:00";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
