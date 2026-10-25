using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>"Rest" for null (a rest day — NenContractRecapDto.DailyBreakdown's own invariant
/// is that Percentage is null exactly when IsRestDay is true, so null alone is enough to
/// decide this), otherwise a plain "50%"/"133%" — uncapped, never clamped to 100%.</summary>
public sealed class PercentageToLabelConverter : IValueConverter
{
    public static readonly PercentageToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double percentage ? FormatPercentage(percentage) : "Rest";

    private static string FormatPercentage(double percentage) => $"{percentage * 100:0}%";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
