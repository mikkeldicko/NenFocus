using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NenFocus.Application.DTOs;

namespace NenFocus.Converters;

/// <summary>
/// The Planner's contract banner: "Contract: Exam month — Day 2/7 — 3.5h / 10h completed — 1h 30m
/// deficit — 45 min bonus", the deficit and bonus only when there are any. One format for every
/// strictness rule. Built in the ViewModel until Gap 89 Slice B.
/// </summary>
public sealed class ContractRangeToBannerConverter : IValueConverter
{
    public static readonly ContractRangeToBannerConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NenContractRangeViewDto c) return string.Empty;

        var text = $"Contract: {c.Name} — Day {c.DayNumber}/{c.TotalDays} — "
                   + $"{HoursToStringConverter.FormatHours(c.TotalCompletedHours)} / {HoursToStringConverter.FormatHours(c.TotalCommittedHours)} completed";
        if (c.Deficit.Minutes > 0) text += $" — {MinutesToStringConverter.FormatMinutes(c.Deficit.Minutes)} deficit";
        if (c.TotalBonusHours > 0) text += $" — {MinutesToStringConverter.FormatMinutes(c.TotalBonusHours * 60)} bonus";
        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
