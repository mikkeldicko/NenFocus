using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class DailyFloorPolicyTests
{
    private static readonly DailyFloor Four = new(4);
    private static readonly DateOnly Monday = new(2026, 7, 6);
    private static readonly DateOnly Wednesday = new(2026, 7, 8);
    private static readonly DateOnly Saturday = new(2026, 7, 11);

    [Fact]
    public void For_NoExceptions_ReturnsDefaultForAnyDate()
    {
        var policy = new DailyFloorPolicy(Four);

        policy.For(Monday).Should().Be(Four);
    }

    [Fact]
    public void For_DateWithNullException_ReturnsNullEvenWhenDefaultIsSet()
    {
        var policy = new DailyFloorPolicy(Four, new Dictionary<DateOnly, DailyFloor?> { [Saturday] = null });

        policy.For(Saturday).Should().BeNull();
    }

    [Fact]
    public void For_NoDefaultAndNoException_ReturnsNull()
    {
        var policy = new DailyFloorPolicy(null);

        policy.For(Monday).Should().BeNull();
    }

    [Fact]
    public void None_HasNoFloorOnAnyDay()
    {
        DailyFloorPolicy.None.For(Monday).Should().BeNull();
    }

    [Fact]
    public void Fixed_AppliesDefaultToEveryDayExceptRestDays()
    {
        var policy = DailyFloorPolicy.Fixed(Four, [Saturday]);

        policy.For(Monday).Should().Be(Four);
        policy.For(Wednesday).Should().Be(Four);
        policy.For(Saturday).Should().BeNull();
    }

    [Fact]
    public void Fixed_NoRestDaysGiven_AppliesDefaultEverywhere()
    {
        var policy = DailyFloorPolicy.Fixed(Four);

        policy.For(Saturday).Should().Be(Four);
    }

    [Fact]
    public void PerDay_EachDayGetsItsOwnFloor_AbsentDayIsARestDay()
    {
        var policy = DailyFloorPolicy.PerDay(new Dictionary<DateOnly, double>
        {
            [Monday] = 2,
            [Wednesday] = 3
        });

        policy.For(Monday)!.Hours.Should().Be(2);
        policy.For(Wednesday)!.Hours.Should().Be(3);
        policy.For(Saturday).Should().BeNull();
    }

    [Fact]
    public void RestDayExceptions_MapsEachDateToNull()
    {
        var exceptions = DailyFloorPolicy.RestDayExceptions([Monday, Wednesday]);

        exceptions.Should().ContainKey(Monday).WhoseValue.Should().BeNull();
        exceptions.Should().ContainKey(Wednesday).WhoseValue.Should().BeNull();
    }

    [Fact]
    public void RestDayExceptions_Empty_ReturnsEmptyMap()
    {
        DailyFloorPolicy.RestDayExceptions([]).Should().BeEmpty();
    }

    [Fact]
    public void PerDayExceptionsFrom_MapsEachDateToItsOwnFloor()
    {
        var exceptions = DailyFloorPolicy.PerDayExceptionsFrom(new Dictionary<DateOnly, double>
        {
            [Monday] = 2,
            [Wednesday] = 3
        });

        exceptions[Monday]!.Hours.Should().Be(2);
        exceptions[Wednesday]!.Hours.Should().Be(3);
    }

    [Fact]
    public void Extend_MergingRestDayExceptionsIntoAnExistingPolicy_ProducesTheSameResultAsFixed()
    {
        // The whole point of exposing these as standalone helpers: Extend merges into an
        // existing policy's PerDayExceptions rather than building a fresh one via Fixed(...),
        // but the conversion itself must be identical either way.
        var viaFixed = DailyFloorPolicy.Fixed(Four, [Saturday]);
        var merged = new DailyFloorPolicy(Four, DailyFloorPolicy.RestDayExceptions([Saturday]));

        merged.For(Saturday).Should().Be(viaFixed.For(Saturday));
        merged.For(Monday).Should().Be(viaFixed.For(Monday));
    }
}
