using System.Globalization;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Converters;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Tests.Converters;

/// <summary>
/// Display text the ViewModels used to build themselves, now made by converters (Gap 89 Slice B,
/// step 6). Each expected string is the one the screen showed before the move.
/// </summary>
public class DisplayTextConverterTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static object? One(Avalonia.Data.Converters.IValueConverter c, object? value, string? parameter = null) =>
        c.Convert(value, typeof(string), parameter, Invariant);

    private static object? Many(Avalonia.Data.Converters.IMultiValueConverter c, params object?[] values) =>
        c.Convert(values, typeof(string), null, Invariant);

    [Fact]
    public void ADateRange_ReadsWithItsYears()
        => Many(DateRangeToLabelConverter.Instance, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))
            .Should().Be("Jan 1, 2026 – Jan 31, 2026");

    [Fact]
    public void AWorkHoursWindow_ReadsAsTwoTimes_OrNoConstraint()
    {
        Many(WorkHoursRangeToLabelConverter.Instance, new TimeOnly(9, 0), new TimeOnly(17, 0)).Should().Be("09:00 – 17:00");
        Many(WorkHoursRangeToLabelConverter.Instance, null, null).Should().Be("No constraint");
    }

    [Fact]
    public void ADayOnItsOwn_TakesTheGivenFormat()
        => One(new DateTimeToStringConverter(), new DateOnly(2026, 3, 5), "MMM d").Should().Be("Mar 5");

    [Theory]
    [InlineData(2.0, 3.0, false, null, DayTiming.Today, false, "2h / 3h")]       // drafting or today: both numbers
    [InlineData(2.0, null, false, null, DayTiming.Today, false, "2h")]           // nothing required
    [InlineData(0.0, null, true, null, DayTiming.Future, false, "Rest")]
    [InlineData(2.5, 4.0, false, StrictnessRule.PeriodTimeBudget, DayTiming.Past, true, "2.5h")] // whole-period target, past
    [InlineData(3.0, 3.0, false, StrictnessRule.DailyFlexibility, DayTiming.Future, false, "3h")] // sealed future day: just the plan
    [InlineData(4.0, 3.0, false, StrictnessRule.DailyFlexibility, DayTiming.Future, false, "4h / 3h")] // planned above the floor
    public void ADaysFloorBadge_ShowsWhatItShowedBefore(
        double current, double? required, bool rest, StrictnessRule? rule, DayTiming timing, bool periodTarget, string expected)
    {
        var day = new DayFloorPreviewViewModel(new DateOnly(2026, 3, 5), current, required, rest, timing, rule, periodTarget);

        One(DayFloorStatusToLabelConverter.Instance, day).Should().Be(expected);
    }

    [Fact]
    public void ADaysBonusPill_ReadsAsHoursOfBonus()
    {
        var day = new DayFloorPreviewViewModel(new DateOnly(2026, 3, 5), 2, 3, false, BonusHours: 1.5);

        One(DayFloorStatusToBonusLabelConverter.Instance, day).Should().Be("+1.5h bonus");
    }

    [Fact]
    public void NoDay_ReadsAsNothing()
    {
        One(DayFloorStatusToLabelConverter.Instance, null).Should().Be(string.Empty);
        One(DayFloorStatusToBonusLabelConverter.Instance, null).Should().Be(string.Empty);
    }

    [Fact]
    public void RestDays_ReadAsTheirChipsDo_OrNone()
    {
        // The wizard summary used to join the dates' default ToString() (the culture's short date).
        One(DateListToLabelConverter.Instance, new[] { new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12) }).Should().Be("Mar 5, Mar 12");
        One(DateListToLabelConverter.Instance, Array.Empty<DateOnly>()).Should().Be("None");
    }

    [Fact]
    public void TheContractBanner_NamesTheDayTheHoursTheDeficitAndTheBonus()
    {
        var contract = new NenContractRangeViewDto
        {
            Name = "Exam month", DayNumber = 2, TotalDays = 7,
            TotalCompletedHours = 3.5, TotalCommittedHours = 10,
            Deficit = WholeMinutes.Of(90), TotalBonusHours = 0.75,
        };

        One(ContractRangeToBannerConverter.Instance, contract)
            .Should().Be("Contract: Exam month — Day 2/7 — 3.5h / 10h completed — 1h 30m deficit — 45 min bonus");
    }

    [Fact]
    public void TheContractBanner_LeavesOutANoughtDeficitAndBonus()
    {
        var contract = new NenContractRangeViewDto
        {
            Name = "Exam month", DayNumber = 1, TotalDays = 7, TotalCompletedHours = 0, TotalCommittedHours = 10,
            Deficit = WholeMinutes.Zero, TotalBonusHours = 0,
        };

        One(ContractRangeToBannerConverter.Instance, contract).Should().Be("Contract: Exam month — Day 1/7 — 0h / 10h completed");
        One(ContractRangeToBannerConverter.Instance, null).Should().Be(string.Empty);
    }
}
