using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class FocusBlockOutcomeToLabelConverter : IValueConverter
{
    public static readonly FocusBlockOutcomeToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FocusBlockOutcome.Missed => "MISSED",
            FocusBlockOutcome.StartedLate => "STARTED LATE",
            FocusBlockOutcome.EndedEarly => "ENDED EARLY",
            FocusBlockOutcome.CompletedOnTime => "COMPLETED ON TIME",
            _ => string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
