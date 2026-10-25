using FluentAssertions;
using NenFocus.Domain.Entities;
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
    public void Constructor_AsLongAsAFocusBlock_IsAllowed()
    {
        // One rule for every focus period: FocusBlock.MaxLength (Gap 89 Slice B, A1).
        var duration = new SessionDuration(FocusBlock.MaxLength);

        duration.Value.Should().Be(FocusBlock.MaxLength);
    }

    [Fact]
    public void Constructor_UnderOneMinute_Throws()
    {
        var act = () => new SessionDuration(TimeSpan.FromSeconds(30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_LongerThanAFocusBlock_Throws()
    {
        var act = () => new SessionDuration(FocusBlock.MaxLength.Add(TimeSpan.FromMinutes(1)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromHoursAndMinutes_BuildsExpectedTimeSpan()
    {
        var duration = SessionDuration.FromHoursAndMinutes(1, 30);

        duration.Value.Should().Be(new TimeSpan(1, 30, 0));
    }
}
