using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class ContractRulesTests
{
    private static ContractRules Rules(StrictnessRule strictness, DailyFloor? fixedDailyFloor) =>
        new(strictness, fixedDailyFloor, allowCancellation: true, keepBlockingDuringBreaks: false,
            RedemptionPlacementPolicy.AnywhereInContract, bonusBlocksFollowStrictness: false);

    [Theory]
    [InlineData(StrictnessRule.Locked)]
    [InlineData(StrictnessRule.DailyFlexibility)]
    [InlineData(StrictnessRule.PeriodTimeBudget)]
    public void FloorRule_WithAFixedDailyFloor_IsFixed_WhateverTheStrictness(StrictnessRule strictness)
    {
        Rules(strictness, new DailyFloor(3)).FloorRule
            .Should().Be(new DailyFloorRule.Fixed(new DailyFloor(3)));
    }

    [Theory]
    [InlineData(StrictnessRule.Locked)]
    [InlineData(StrictnessRule.DailyFlexibility)]
    public void FloorRule_WithoutAFixedFloor_FollowsThePlan(StrictnessRule strictness)
    {
        Rules(strictness, null).FloorRule.Should().Be(new DailyFloorRule.FollowsPlan());
    }

    [Fact]
    public void FloorRule_WithoutAFixedFloor_UnderPeriodTimeBudget_IsNone()
    {
        // No per-day obligation at all — the contract carries a whole-period target instead.
        Rules(StrictnessRule.PeriodTimeBudget, null).FloorRule.Should().Be(new DailyFloorRule.None());
    }

    [Theory]
    [InlineData(StrictnessRule.Locked, MovementRange.None)]
    [InlineData(StrictnessRule.DailyFlexibility, MovementRange.OwnDay)]
    [InlineData(StrictnessRule.PeriodTimeBudget, MovementRange.WholePeriod)]
    public void MovementRange_IsWhatTheStrictnessMeans(StrictnessRule strictness, MovementRange expected)
    {
        Rules(strictness, null).MovementRange.Should().Be(expected);
    }

    [Fact]
    public void TwoRulesWithTheSameSettings_AreEqual()
    {
        Rules(StrictnessRule.Locked, new DailyFloor(2)).Should().Be(Rules(StrictnessRule.Locked, new DailyFloor(2)));
    }

    // ── A fixed floor is whole hours, 1–24 (Gap 92, V9) ──

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(24)]
    public void AFixedFloorOfWholeHoursFrom1To24_IsAccepted(double hours)
        => Rules(StrictnessRule.Locked, new DailyFloor(hours)).FixedDailyFloor!.Hours.Should().Be(hours);

    [Theory]
    [InlineData(0)]   // "no floor" is null, never 0
    [InlineData(1.5)] // a fixed floor is whole hours
    public void AFixedFloorThatIsntWholeHoursFrom1To24_IsRefused(double hours)
    {
        var act = () => Rules(StrictnessRule.Locked, new DailyFloor(hours));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(24, true)]
    [InlineData(0, false)]
    [InlineData(25, false)]
    public void IsValidFixedFloor_IsWholeHoursFrom1To24(int hours, bool expected)
        => ContractRules.IsValidFixedFloor(hours).Should().Be(expected);
}
