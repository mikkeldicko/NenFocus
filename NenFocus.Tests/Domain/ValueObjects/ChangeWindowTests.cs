using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class ChangeWindowTests
{
    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        var act = () => new ChangeWindow(new TimeOnly(12, 0), new TimeOnly(9, 0), 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EndEqualsStart_Throws()
    {
        var act = () => new ChangeWindow(new TimeOnly(9, 0), new TimeOnly(9, 0), 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_UsesPerDayBelowOne_Throws()
    {
        var act = () => new ChangeWindow(new TimeOnly(9, 0), new TimeOnly(12, 0), 0);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(9, 0, true)]    // at start boundary
    [InlineData(10, 30, true)]  // mid-window
    [InlineData(12, 0, false)]  // at end boundary (exclusive)
    [InlineData(8, 59, false)]  // before start
    [InlineData(13, 0, false)]  // after end
    public void Contains_VariousTimes(int hour, int minute, bool expected)
    {
        var window = new ChangeWindow(new TimeOnly(9, 0), new TimeOnly(12, 0), 2);

        window.Contains(new TimeOnly(hour, minute)).Should().Be(expected);
    }
}
