using FluentAssertions;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.Services;

/// <summary>
/// Hours per the user's local day, in real time (Gap 88 phase 4). Times below are Copenhagen
/// wall-clock times. A zero-length or inverted range can't be passed any more — a TimeSlot
/// refuses to exist that way.
/// </summary>
public class PlannedFocusHoursCalculatorTests
{
    private static readonly UserTimeZone Copenhagen = new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));
    private readonly PlannedFocusHoursCalculator _calculator = new();

    private static TimeSlot Local(DateTime start, DateTime end) => new(Copenhagen.ToMoment(start), Copenhagen.ToMoment(end));

    private IReadOnlyDictionary<DateOnly, double> Calculate(params TimeSlot[] slots) => _calculator.Calculate(slots, Copenhagen);

    [Fact]
    public void Calculate_NoRanges_ReturnsEmpty()
        => Calculate().Should().BeEmpty();

    [Fact]
    public void Calculate_SingleRangeWithinOneDay_CreditsThatDay()
        => Calculate(Local(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)))
            .Should().ContainKey(new DateOnly(2026, 1, 1)).WhoseValue.Should().Be(2);

    [Fact]
    public void Calculate_MultipleRangesSameDay_Sums()
    {
        var result = Calculate(
            Local(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)),
            Local(new DateTime(2026, 1, 1, 14, 0, 0), new DateTime(2026, 1, 1, 16, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(3);
    }

    [Fact]
    public void Calculate_RangesOnDifferentDays_GroupedSeparately()
    {
        var result = Calculate(
            Local(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)),
            Local(new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 12, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(1);
        result[new DateOnly(2026, 1, 2)].Should().Be(3);
    }

    [Fact]
    public void Calculate_RangeCrossingMidnight_SplitsProportionally()
    {
        var result = Calculate(Local(new DateTime(2026, 1, 1, 23, 0, 0), new DateTime(2026, 1, 2, 1, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(1);
        result[new DateOnly(2026, 1, 2)].Should().Be(1);
    }

    [Fact]
    public void Calculate_RangeSpanningMultipleFullDays_SplitsAcrossEachDay()
    {
        var result = Calculate(Local(new DateTime(2026, 1, 1, 22, 0, 0), new DateTime(2026, 1, 3, 2, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(2);   // 22:00-24:00
        result[new DateOnly(2026, 1, 2)].Should().Be(24);  // full day
        result[new DateOnly(2026, 1, 3)].Should().Be(2);   // 00:00-02:00
    }

    [Fact]
    public void Calculate_JustAfterLocalMidnight_CountsOnTheLocalDay()
        // 00:30–01:30 local is the previous day in UTC; the user's day is what counts.
        => Calculate(Local(new DateTime(2026, 1, 2, 0, 30, 0), new DateTime(2026, 1, 2, 1, 30, 0)))
            .Should().Equal(new Dictionary<DateOnly, double> { [new DateOnly(2026, 1, 2)] = 1 });

    [Fact]
    public void Calculate_SpringForwardNight_CountsTheRealHours()
        // 01:00–04:00 on 29 March 2026 reads as 3 hours but lasts 2: 02:00 jumps to 03:00.
        => Calculate(Local(new DateTime(2026, 3, 29, 1, 0, 0), new DateTime(2026, 3, 29, 4, 0, 0)))
            [new DateOnly(2026, 3, 29)].Should().Be(2);

    [Fact]
    public void Calculate_FallBackNight_CountsTheRealHours()
        // 01:00–04:00 on 25 October 2026 reads as 3 hours but lasts 4: 03:00 falls back to 02:00.
        => Calculate(Local(new DateTime(2026, 10, 25, 1, 0, 0), new DateTime(2026, 10, 25, 4, 0, 0)))
            [new DateOnly(2026, 10, 25)].Should().Be(4);
}
