using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>Plain "{0.#}h" decimal-hours format (e.g. "2.5h") — distinct from
/// MinutesToStringConverter's compound "Xh Ym" style, used wherever a day badge or bonus
/// pill shows a single decimal-hours figure rather than a breakdown.</summary>
public sealed class HoursToStringConverter : IValueConverter
{
    public static readonly HoursToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double hours ? FormatHours(hours) : string.Empty;

    /// <summary>For converters that word a whole sentence (the contract banner, the day badges) —
    /// never called from a ViewModel (ViewModelRuleTests.NoViewModelFormatsValues).</summary>
    public static string FormatHours(double hours) => $"{hours:0.#}h";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
