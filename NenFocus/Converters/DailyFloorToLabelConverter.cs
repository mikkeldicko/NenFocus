using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>"1.5h / day" for a set daily floor, "Derived from scheduled blocks" when none is
/// fixed — was independently hand-rolled in ContractPresetCardViewModel, ContractRulesStepViewModel,
/// and ContractWizardViewModel; consolidated here so the three copies can't drift apart.</summary>
public sealed class DailyFloorToLabelConverter : IValueConverter
{
    public static readonly DailyFloorToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => FormatFloor(value as double?);

    private static string FormatFloor(double? hours)
        => hours is { } floor ? $"{floor:0.##}h / day" : "Derived from scheduled blocks";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
