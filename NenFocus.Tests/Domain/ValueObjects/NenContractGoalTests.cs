using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class NenContractGoalTests
{
    [Fact]
    public void Constructor_TrimsValue()
    {
        var goal = new NenContractGoal("  Ship the feature this month  ");

        goal.Value.Should().Be("Ship the feature this month");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankValue_Throws(string? blank)
    {
        var act = () => new NenContractGoal(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var goal = new NenContractGoal("Write code every day");

        goal.ToString().Should().Be("Write code every day");
    }
}
