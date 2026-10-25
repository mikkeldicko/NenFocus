using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

public sealed class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "h:mm tt";
        return value switch
        {
            DateTime dt => dt.ToString(format, culture),
            DateOnly date => date.ToString(format, culture),   // a calendar day, e.g. "MMM d"
            _ => string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

