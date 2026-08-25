using FluentAssertions;
using NenFocus.Domain.Services;

namespace NenFocus.Tests.Domain.Services;

public class DayRangeSplitterTests
{
    [Fact]
    public void Split_ZeroLengthOrInvertedRange_YieldsNothing()
    {
        var sameInstant = new DateTime(2026, 1, 1, 9, 0, 0);

        DayRangeSplitter.Split(sameInstant, sameInstant).Should().BeEmpty();
        DayRangeSplitter.Split(sameInstant.AddHours(2), sameInstant).Should().BeEmpty();
    }

    [Fact]
    public void Split_RangeWithinOneDay_YieldsOneSegmentUnchanged()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var end = new DateTime(2026, 1, 1, 11, 0, 0);

        var segments = DayRangeSplitter.Split(start, end).ToList();

        segments.Should().ContainSingle();
        segments[0].Date.Should().Be(new DateOnly(2026, 1, 1));
        segments[0].SegmentStart.Should().Be(start);
        segments[0].SegmentEnd.Should().Be(end);
    }

    [Fact]
    public void Split_RangeCrossingMidnight_YieldsTwoSegmentsMeetingAtMidnight()
    {
        var start = new DateTime(2026, 1, 1, 23, 0, 0);
        var end = new DateTime(2026, 1, 2, 1, 0, 0);
        var midnight = new DateTime(2026, 1, 2, 0, 0, 0);

        var segments = DayRangeSplitter.Split(start, end).ToList();

        segments.Should().HaveCount(2);
        segments[0].Should().Be((new DateOnly(2026, 1, 1), start, midnight));
        segments[1].Should().Be((new DateOnly(2026, 1, 2), midnight, end));
    }

    [Fact]
    public void Split_RangeSpanningMultipleFullDays_YieldsOneSegmentPerDayTouched()
    {
        var start = new DateTime(2026, 1, 1, 22, 0, 0);
        var end = new DateTime(2026, 1, 3, 2, 0, 0); // touches Jan 1, 2, and 3

        var segments = DayRangeSplitter.Split(start, end).ToList();

        segments.Should().HaveCount(3);
        segments[0].Date.Should().Be(new DateOnly(2026, 1, 1));
        segments[0].SegmentStart.Should().Be(start);
        segments[0].SegmentEnd.Should().Be(new DateTime(2026, 1, 2, 0, 0, 0));

        segments[1].Date.Should().Be(new DateOnly(2026, 1, 2));
        segments[1].SegmentStart.Should().Be(new DateTime(2026, 1, 2, 0, 0, 0));
        segments[1].SegmentEnd.Should().Be(new DateTime(2026, 1, 3, 0, 0, 0));

        segments[2].Date.Should().Be(new DateOnly(2026, 1, 3));
        segments[2].SegmentStart.Should().Be(new DateTime(2026, 1, 3, 0, 0, 0));
        segments[2].SegmentEnd.Should().Be(end);
    }

    [Fact]
    public void Split_RangeEndingExactlyAtMidnight_HasNoPresenceOnTheNextDay()
    {
        var start = new DateTime(2026, 1, 1, 15, 0, 0);
        var end = new DateTime(2026, 1, 2, 0, 0, 0); // 15:00-24:00

        var segments = DayRangeSplitter.Split(start, end).ToList();

        segments.Should().ContainSingle();
        segments[0].Date.Should().Be(new DateOnly(2026, 1, 1));
        segments[0].SegmentEnd.Should().Be(end);
    }

    [Fact]
    public void Split_IsKindAgnostic_TreatsWhateverDateTimeItsGivenAsCalendarTruth()
    {
        var start = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc);

        var segments = DayRangeSplitter.Split(start, end).ToList();

        // Splits by the raw .Date component regardless of Kind — callers decide what
        // "calendar day" should mean by converting before calling, not this primitive.
        segments.Should().HaveCount(2);
    }
}
