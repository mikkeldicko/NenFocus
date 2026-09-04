using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.Converters;

public sealed class RedemptionPlacementPolicyToLabelConverter : IValueConverter
{
    public static readonly RedemptionPlacementPolicyToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RedemptionPlacementPolicy.AnywhereInContract => "Anywhere in the contract",
        RedemptionPlacementPolicy.CurrentDayOnly => "Current day only",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
