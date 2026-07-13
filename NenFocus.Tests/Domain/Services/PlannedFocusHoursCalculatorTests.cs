using FluentAssertions;
using NenFocus.Domain.Services;

namespace NenFocus.Tests.Domain.Services;

public class PlannedFocusHoursCalculatorTests
{
    private readonly PlannedFocusHoursCalculator _calculator = new();

    [Fact]
    public void Calculate_NoRanges_ReturnsEmpty()
    {
        var result = _calculator.Calculate([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_SingleRangeWithinOneDay_CreditsThatDay()
    {
        var day = new DateOnly(2026, 1, 1);
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var end = new DateTime(2026, 1, 1, 11, 0, 0);

        var result = _calculator.Calculate([(start, end)]);

        result.Should().ContainKey(day).WhoseValue.Should().Be(2);
    }

    [Fact]
    public void Calculate_MultipleRangesSameDay_Sums()
    {
        var day = new DateOnly(2026, 1, 1);
        var ranges = new[]
        {
            (new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)),
            (new DateTime(2026, 1, 1, 14, 0, 0), new DateTime(2026, 1, 1, 16, 0, 0))
        };

        var result = _calculator.Calculate(ranges);

        result[day].Should().Be(3);
    }

    [Fact]
    public void Calculate_RangesOnDifferentDays_GroupedSeparately()
    {
        var day1 = new DateOnly(2026, 1, 1);
        var day2 = new DateOnly(2026, 1, 2);
        var ranges = new[]
        {
            (new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)),
            (new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 12, 0, 0))
        };

        var result = _calculator.Calculate(ranges);

        result[day1].Should().Be(1);
        result[day2].Should().Be(3);
    }

    [Fact]
    public void Calculate_RangeCrossingMidnight_SplitsProportionally()
    {
        var day1 = new DateOnly(2026, 1, 1);
        var day2 = new DateOnly(2026, 1, 2);
        var start = new DateTime(2026, 1, 1, 23, 0, 0);
        var end = new DateTime(2026, 1, 2, 1, 0, 0);

        var result = _calculator.Calculate([(start, end)]);

        result[day1].Should().Be(1);
        result[day2].Should().Be(1);
    }

    [Fact]
    public void Calculate_RangeSpanningMultipleFullDays_SplitsAcrossEachDay()
    {
        var start = new DateTime(2026, 1, 1, 22, 0, 0);
        var end = new DateTime(2026, 1, 3, 2, 0, 0); // touches Jan 1, 2, and 3

        var result = _calculator.Calculate([(start, end)]);

        result[new DateOnly(2026, 1, 1)].Should().Be(2);   // 22:00-24:00
        result[new DateOnly(2026, 1, 2)].Should().Be(24);  // full day
        result[new DateOnly(2026, 1, 3)].Should().Be(2);   // 00:00-02:00
    }

    [Fact]
    public void Calculate_ZeroLengthOrInvertedRange_Ignored()
    {
        var sameInstant = new DateTime(2026, 1, 1, 9, 0, 0);
        var inverted = (new DateTime(2026, 1, 1, 11, 0, 0), new DateTime(2026, 1, 1, 9, 0, 0));

        var result = _calculator.Calculate([(sameInstant, sameInstant), inverted]);

        result.Should().BeEmpty();
    }
}
