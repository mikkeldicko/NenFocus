using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class SessionGoalTests
{
    [Fact]
    public void Constructor_TrimsValue()
    {
        var goal = new SessionGoal("  Finish the report  ");

        goal.Value.Should().Be("Finish the report");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankValue_Throws(string? blank)
    {
        var act = () => new SessionGoal(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var goal = new SessionGoal("Write code");

        goal.ToString().Should().Be("Write code");
    }
}
