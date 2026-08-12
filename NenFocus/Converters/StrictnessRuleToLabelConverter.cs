using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class StrictnessRuleToLabelConverter : IValueConverter
{
    public static readonly StrictnessRuleToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StrictnessRule.Locked => "Lockdown",
        StrictnessRule.DailyFlexibility => "Daily Flex",
        StrictnessRule.PeriodTimeBudget => "Period Study",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
