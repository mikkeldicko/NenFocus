using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class DailyFloorTests
{
    [Fact]
    public void Constructor_Negative_Throws()
    {
        var act = () => new DailyFloor(-1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_AboveTwentyFourHours_Throws()
    {
        var act = () => new DailyFloor(24.1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_TwentyFourHours_DoesNotThrow()
    {
        var act = () => new DailyFloor(24);

        act.Should().NotThrow();
    }

    [Fact]
    public void None_HasZeroHours()
    {
        DailyFloor.None.Hours.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(4, 3.9, false)]
    [InlineData(4, 4, true)]
    [InlineData(4, 5, true)]
    public void IsSatisfiedBy_ComparesAgainstHours(double floorHours, double focusHours, bool expected)
    {
        var floor = new DailyFloor(floorHours);

        floor.IsSatisfiedBy(focusHours).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(24)]
    public void IsSatisfiedBy_WhenNone_SatisfiedByAnyValue(double focusHours)
    {
        DailyFloor.None.IsSatisfiedBy(focusHours).Should().BeTrue();
    }

    [Theory]
    [InlineData(4, 0, 4)]
    [InlineData(4, 1.5, 2.5)]
    [InlineData(4, 4, 0)]
    [InlineData(4, 5, 0)]   // exceeding the floor clamps to zero, never negative
    public void HoursRemaining_ClampsAtZero(double floorHours, double focusHours, double expected)
    {
        var floor = new DailyFloor(floorHours);

        floor.HoursRemaining(focusHours).Should().Be(expected);
    }

    [Fact]
    public void PercentComplete_WhenNone_ReturnsNull()
    {
        DailyFloor.None.PercentComplete(2).Should().BeNull();
    }

    [Theory]
    [InlineData(4, 0, 0.0)]
    [InlineData(4, 2, 0.5)]
    [InlineData(4, 4, 1.0)]
    [InlineData(4, 5.32, 1.33)] // uncapped — 133% displays as 133%, never clamped to 100%
    public void PercentComplete_DividesCompletedByHours(double floorHours, double completedHours, double expected)
    {
        var floor = new DailyFloor(floorHours);

        floor.PercentComplete(completedHours).Should().BeApproximately(expected, 0.001);
    }
}
