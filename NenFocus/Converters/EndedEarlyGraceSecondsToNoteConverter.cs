using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

public sealed class EndedEarlyGraceSecondsToNoteConverter : IValueConverter
{
    public static readonly EndedEarlyGraceSecondsToNoteConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double seconds
            ? $"Ended {(int)Math.Round(seconds)}s early — within grace, full credit given."
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
