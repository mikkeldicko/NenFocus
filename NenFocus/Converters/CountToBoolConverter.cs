using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// Converts an int count to a bool. <c>IsZero</c> returns true when the count is 0.
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public static readonly CountToBoolConverter IsZero = new() { _mode = Mode.IsZero };

    private enum Mode { IsZero }
    private Mode _mode = Mode.IsZero;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0
        };
        return _mode switch
        {
            Mode.IsZero => count == 0,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
