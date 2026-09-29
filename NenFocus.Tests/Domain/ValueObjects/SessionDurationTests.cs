using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class SessionDurationTests
{
    [Fact]
    public void Constructor_OneMinute_IsAllowed()
    {
        var duration = new SessionDuration(TimeSpan.FromMinutes(1));

        duration.Value.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Constructor_TwentyFourHours_IsAllowed()
    {
        var duration = new SessionDuration(TimeSpan.FromHours(24));

        duration.Value.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Constructor_UnderOneMinute_Throws()
    {
        var act = () => new SessionDuration(TimeSpan.FromSeconds(30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_OverTwentyFourHours_Throws()
    {
        var act = () => new SessionDuration(TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromHoursAndMinutes_BuildsExpectedTimeSpan()
    {
        var duration = SessionDuration.FromHoursAndMinutes(1, 30);

        duration.Value.Should().Be(new TimeSpan(1, 30, 0));
    }
}
