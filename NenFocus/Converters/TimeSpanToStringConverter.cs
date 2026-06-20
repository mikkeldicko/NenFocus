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
            // Use provided format or default to standard hh\:mm\:ss
            string format = parameter as string ?? @"hh\:mm\:ss";

            try
            {
                return ts.ToString(format, culture);
            }
            catch (FormatException)
            {
                // DEEP MODULE PROTECTION: 
                // If a developer provides "mm:ss" without escaping the colon, 
                // we fix it automatically instead of crashing the app.
                if (format.Contains(":") && !format.Contains(@"\:"))
                {
                    return ts.ToString(format.Replace(":", @"\:"), culture);
                }

                return ts.ToString(@"hh\:mm\:ss", culture);
            }
        }

        return "00:00:00";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
} // <--- This closing brace for the class was missing
