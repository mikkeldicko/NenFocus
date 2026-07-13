using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class WorkHoursPolicyTests
{
    private static readonly WorkHours NineToFive = new(new TimeOnly(9, 0), new TimeOnly(17, 0));
    private static readonly WorkHours ElevenToEight = new(new TimeOnly(11, 0), new TimeOnly(20, 0));
    private static readonly DateOnly Wednesday = new(2026, 7, 8);
    private static readonly DateOnly Saturday = new(2026, 7, 11);
    private static readonly DateOnly Monday = new(2026, 7, 6);

    [Fact]
    public void For_NoExceptions_ReturnsDefaultForAnyDate()
    {
        var policy = new WorkHoursPolicy(NineToFive);

        policy.For(Monday).Should().Be(NineToFive);
    }

    [Fact]
    public void For_DateWithOverrideException_ReturnsTheOverride()
    {
        var policy = new WorkHoursPolicy(NineToFive, new Dictionary<DateOnly, WorkHours?>
        {
            [Wednesday] = ElevenToEight
        });

        policy.For(Wednesday).Should().Be(ElevenToEight);
    }

    [Fact]
    public void For_DateWithNullException_ReturnsNullEvenWhenDefaultIsSet()
    {
        var policy = new WorkHoursPolicy(NineToFive, new Dictionary<DateOnly, WorkHours?>
        {
            [Saturday] = null
        });

        policy.For(Saturday).Should().BeNull();
    }

    [Fact]
    public void For_DateNotInExceptions_FallsBackToDefault()
    {
        var policy = new WorkHoursPolicy(NineToFive, new Dictionary<DateOnly, WorkHours?>
        {
            [Wednesday] = ElevenToEight
        });

        policy.For(Monday).Should().Be(NineToFive);
    }

    [Fact]
    public void For_NoDefaultAndNoException_ReturnsNull()
    {
        var policy = new WorkHoursPolicy(null);

        policy.For(Monday).Should().BeNull();
    }

    [Fact]
    public void NoConstraintExceptions_MapsEachDateToNull()
    {
        var exceptions = WorkHoursPolicy.NoConstraintExceptions([Monday, Wednesday]);

        exceptions.Should().ContainKey(Monday).WhoseValue.Should().BeNull();
        exceptions.Should().ContainKey(Wednesday).WhoseValue.Should().BeNull();
    }

    [Fact]
    public void NoConstraintExceptions_Empty_ReturnsEmptyMap()
    {
        WorkHoursPolicy.NoConstraintExceptions([]).Should().BeEmpty();
    }

    [Fact]
    public void NoConstraintExceptions_MergedIntoPolicy_ResolvesAsNoConstraintOnThatDay()
    {
        var policy = new WorkHoursPolicy(NineToFive, WorkHoursPolicy.NoConstraintExceptions([Saturday]));

        policy.For(Saturday).Should().BeNull();
        policy.For(Monday).Should().Be(NineToFive);
    }
}
