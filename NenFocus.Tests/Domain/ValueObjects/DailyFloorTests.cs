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

    // ── One floor-vs-window rule (Gap 92, V9) ──

    [Theory]
    [InlineData(3, 3, true)]   // exactly the window
    [InlineData(3, 2.5, false)]
    [InlineData(1.5, 2, true)] // a plan-following floor may be fractional
    public void FitsIn_TheWindowIsAtLeastTheFloor(double floorHours, double windowHours, bool expected)
    {
        new DailyFloor(floorHours).FitsIn(TimeSpan.FromHours(windowHours)).Should().Be(expected);
    }
}
