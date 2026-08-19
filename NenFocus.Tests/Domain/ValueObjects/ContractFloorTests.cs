using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class ContractFloorTests
{
    [Fact]
    public void Constructor_Negative_Throws()
    {
        var act = () => new ContractFloor(-1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_AboveTwentyFourHours_DoesNotThrow()
    {
        // Unlike DailyFloor, a whole-period total has no realistic upper bound.
        var act = () => new ContractFloor(40);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(9, 8.9, false)]
    [InlineData(9, 9, true)]
    [InlineData(9, 10, true)]
    public void IsSatisfiedBy_ComparesAgainstHours(double floorHours, double focusHours, bool expected)
    {
        var floor = new ContractFloor(floorHours);

        floor.IsSatisfiedBy(focusHours).Should().Be(expected);
    }
}
