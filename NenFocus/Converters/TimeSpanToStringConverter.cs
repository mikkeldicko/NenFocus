using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

public sealed class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            string format = parameter as string ?? @"hh\:mm\:ss";

            try
            {
                return ts.ToString(format, culture);
            }
            catch (FormatException)
            {
                // If the format contains an unescaped colon (e.g. "mm:ss" instead of @"mm\:ss"),
                // fix it automatically rather than crashing the binding.
                if (format.Contains(":") && !format.Contains(@"\:"))
                    return ts.ToString(format.Replace(":", @"\:"), culture);

                return ts.ToString(@"hh\:mm\:ss", culture);
            }
        }

        return "00:00:00";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
