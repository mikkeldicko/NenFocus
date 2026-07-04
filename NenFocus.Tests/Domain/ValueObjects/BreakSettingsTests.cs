using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class BreakSettingsTests
{
    [Theory]
    [InlineData(25, 5)]
    [InlineData(45, 10)]
    [InlineData(60, 15)]
    public void Constructor_ValidCombinations_AreAccepted(int interval, int breakLength)
    {
        var settings = new BreakSettings(interval, breakLength);

        settings.IntervalMinutes.Should().Be(interval);
        settings.BreakLengthMinutes.Should().Be(breakLength);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(90)]
    public void Constructor_InvalidInterval_Throws(int interval)
    {
        var act = () => new BreakSettings(interval, 5);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(30)]
    public void Constructor_InvalidBreakLength_Throws(int breakLength)
    {
        var act = () => new BreakSettings(25, breakLength);

        act.Should().Throw<ArgumentException>();
    }
}
