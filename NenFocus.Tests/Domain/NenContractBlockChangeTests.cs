using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

/// <summary>
/// Gap 93: what a contract allows to happen to one of its blocks. Three days, Monday 5 to
/// Wednesday 7 January 2026, Work Hours 09:00–17:00, read in UTC.
/// </summary>
public class NenContractBlockChangeTests
{
    private static readonly DateOnly Monday = new(2026, 1, 5);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly WorkHours NineToFive = new(new TimeOnly(9, 0), new TimeOnly(17, 0));
    private static readonly Moment SealedAt = At.Utc(2026, 1, 4, 12);

    private static NenContract Contract(
        StrictnessRule strictness,
        bool bonusBlocksFollowStrictness = false,
        double? fixedFloorHours = null,
        IReadOnlyDictionary<DateOnly, double>? plan = null)
    {
        // Cancellation on, so a test can finish a contract by breaking it (Break obeys CheckBreak, Gap 92).
        var rules = TestRules.Of(strictness,
            fixedFloorHours is { } hours ? new DailyFloor(hours) : null,
            allowCancellation: true,
            bonusBlocksFollowStrictness: bonusBlocksFollowStrictness);
        plan ??= new Dictionary<DateOnly, double>();
        return NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            rules: rules,
            calendar: TestCalendars.For(Monday, Wednesday, rules.FloorRule, plan, NineToFive),
            plannedFocusHoursByDay: plan,
            sealedAt: SealedAt,
            timeZone: At.UtcZone);
    }

    private static NenContract Finished(StrictnessRule strictness)
    {
        var contract = Contract(strictness);
        contract.Break(At.Utc(2026, 1, 6, 12), At.UtcZone, sessionRunning: false);
        return contract;
    }

    private static TimeSlot Slot(DateOnly day, int fromHour, int toHour) =>
        At.Slot(day.ToDateTime(new TimeOnly(fromHour, 0)), day.ToDateTime(new TimeOnly(0, 0)).AddHours(toHour));

    private static FocusBlock Committed(NenContract contract, TimeSlot slot) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot, membership: TestContracts.Committed(contract.Id));

    private static FocusBlock Redemption(NenContract contract, TimeSlot slot) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot, membership: TestContracts.Redemption(contract.Id));

    private static FocusBlock Bonus(NenContract contract, TimeSlot slot) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot, membership: contract.MembershipForPlacedBlock());

    private static readonly Moment BeforeItAll = At.Utc(2026, 1, 4, 13);

    // ── Binds ────────────────────────────────────────────────────────────────

    [Fact]
    public void Binds_CommittedAndRedemptionBlocks_Always()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);

        contract.Binds(Committed(contract, Slot(Tuesday, 10, 12))).Should().BeTrue();
        contract.Binds(Redemption(contract, Slot(Tuesday, 13, 14))).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Binds_ABonusBlock_OnlyWhenBonusBlocksFollowTheStrictness(bool toggle, bool binds)
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, bonusBlocksFollowStrictness: toggle);

        contract.Binds(Bonus(contract, Slot(Tuesday, 10, 12))).Should().Be(binds);
    }

    [Fact]
    public void AnyQuestion_AboutAnotherContractsBlock_Throws()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);
        var stranger = Committed(Contract(StrictnessRule.DailyFlexibility), Slot(Tuesday, 10, 12));
        var plain = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Slot(Tuesday, 10, 12));

        FluentActions.Invoking(() => contract.Binds(stranger)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => contract.CheckBlockDelete(plain, BeforeItAll)).Should().Throw<ArgumentException>();
    }

    // ── Move ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckBlockMove_UnderLockdown_NoMovementAllowed()
    {
        var contract = Contract(StrictnessRule.Locked);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Tuesday, 13, 15), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.NoMovementAllowed);
    }

    [Fact]
    public void CheckBlockMove_UnderDailyFlex_WithinItsOwnDay_Allowed()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Tuesday, 14, 16), At.UtcZone).Outcome.Should().Be(ChangeOutcome.Allowed);
    }

    [Fact]
    public void CheckBlockMove_UnderDailyFlex_ToAnotherDay_OutsideOwnDay()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Wednesday, 10, 12), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.OutsideOwnDay);
    }

    [Fact]
    public void CheckBlockMove_UnderPeriodTimeBudget_ToAnotherDayOfTheContract_Allowed()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Monday, 10, 12), At.UtcZone).Outcome.Should().Be(ChangeOutcome.Allowed);
    }

    [Theory]
    [InlineData(StrictnessRule.Locked)]
    [InlineData(StrictnessRule.DailyFlexibility)]
    [InlineData(StrictnessRule.PeriodTimeBudget)]
    public void CheckBlockMove_OutOfThePlannedPeriod_Refused_WhateverTheStrictness(StrictnessRule strictness)
    {
        var contract = Contract(strictness);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Wednesday.AddDays(1), 10, 12), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.OutsideContractPeriod);
    }

    [Fact]
    public void CheckBlockMove_ToTheFirstDayBeforeTheContractStarts_IsOutsideThePeriod()
    {
        // Measured in moments, not dates: Monday 07:00 is a contract date but before its start.
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Monday, 7, 9), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.OutsideContractPeriod);
    }

    [Fact]
    public void CheckBlockMove_StraddlingTheContractsStart_IsOutsideThePeriod()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Monday, 8, 10), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.OutsideContractPeriod);
    }

    [Fact]
    public void CheckBlockMove_AFreeBonusBlock_MovesAnywhereInsideThePeriod_EvenUnderLockdown()
    {
        var contract = Contract(StrictnessRule.Locked, bonusBlocksFollowStrictness: false);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Wednesday, 10, 12), At.UtcZone).Outcome.Should().Be(ChangeOutcome.Allowed);
    }

    [Fact]
    public void CheckBlockMove_AFreeBonusBlock_StillNeverLeavesThePeriod()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, bonusBlocksFollowStrictness: false);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Wednesday.AddDays(1), 10, 12), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.OutsideContractPeriod);
    }

    [Fact]
    public void CheckBlockMove_ABindingBonusBlock_FollowsTheStrictness()
    {
        var contract = Contract(StrictnessRule.Locked, bonusBlocksFollowStrictness: true);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockMove(block, Slot(Tuesday, 13, 15), At.UtcZone).Outcome
            .Should().Be(ChangeOutcome.NoMovementAllowed);
    }

    [Fact]
    public void CheckBlockMove_UnderDailyFlex_ABlockAcrossMidnightMayStayOnItsOwnTwoDays()
    {
        // No Work Hours here, so a block can run across midnight at all.
        var rules = TestRules.Of(StrictnessRule.DailyFlexibility);
        var contract = NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(Monday, Wednesday, rules.FloorRule), new Dictionary<DateOnly, double>(), SealedAt, At.UtcZone);
        var block = Committed(contract, At.Slot(new DateTime(2026, 1, 5, 22, 0, 0), new DateTime(2026, 1, 6, 1, 0, 0)));

        contract.CheckBlockMove(block, At.Slot(new DateTime(2026, 1, 5, 23, 0, 0), new DateTime(2026, 1, 6, 2, 0, 0)), At.UtcZone)
            .Outcome.Should().Be(ChangeOutcome.Allowed);
        contract.CheckBlockMove(block, At.Slot(new DateTime(2026, 1, 6, 23, 0, 0), new DateTime(2026, 1, 7, 2, 0, 0)), At.UtcZone)
            .Outcome.Should().Be(ChangeOutcome.OutsideOwnDay);
    }

    // ── Preset, split, delete ────────────────────────────────────────────────

    [Fact]
    public void CheckBlockPresetChange_ABoundBlock_PresetFixed_AFreeBonusBlock_Allowed()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);

        contract.CheckBlockPresetChange(Committed(contract, Slot(Tuesday, 10, 12))).Outcome.Should().Be(ChangeOutcome.PresetFixed);
        contract.CheckBlockPresetChange(Redemption(contract, Slot(Tuesday, 13, 14))).Outcome.Should().Be(ChangeOutcome.PresetFixed);
        contract.CheckBlockPresetChange(Bonus(contract, Slot(Tuesday, 15, 16))).Outcome.Should().Be(ChangeOutcome.Allowed);
    }

    [Fact]
    public void CheckBlockPresetChange_ABindingBonusBlock_PresetFixed()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, bonusBlocksFollowStrictness: true);

        contract.CheckBlockPresetChange(Bonus(contract, Slot(Tuesday, 15, 16))).Outcome.Should().Be(ChangeOutcome.PresetFixed);
    }

    [Theory]
    [InlineData(StrictnessRule.Locked, ChangeOutcome.NoMovementAllowed)]
    [InlineData(StrictnessRule.DailyFlexibility, ChangeOutcome.Allowed)]
    [InlineData(StrictnessRule.PeriodTimeBudget, ChangeOutcome.Allowed)]
    public void CheckBlockSplit_ABoundBlock_ByStrictness(StrictnessRule strictness, ChangeOutcome expected)
    {
        var contract = Contract(strictness);

        contract.CheckBlockSplit(Committed(contract, Slot(Tuesday, 10, 12))).Outcome.Should().Be(expected);
    }

    [Fact]
    public void CheckBlockSplit_AFreeBonusBlock_SplitNotOffered_ABindingOne_ByStrictness()
    {
        var free = Contract(StrictnessRule.DailyFlexibility, bonusBlocksFollowStrictness: false);
        var binding = Contract(StrictnessRule.DailyFlexibility, bonusBlocksFollowStrictness: true);

        free.CheckBlockSplit(Bonus(free, Slot(Tuesday, 10, 12))).Outcome.Should().Be(ChangeOutcome.SplitNotOffered);
        binding.CheckBlockSplit(Bonus(binding, Slot(Tuesday, 10, 12))).Outcome.Should().Be(ChangeOutcome.Allowed);
    }

    [Fact]
    public void CheckBlockDelete_CommittedAndRedemptionBlocks_Never()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);

        contract.CheckBlockDelete(Committed(contract, Slot(Tuesday, 10, 12)), BeforeItAll).Outcome
            .Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
        contract.CheckBlockDelete(Redemption(contract, Slot(Tuesday, 13, 14)), BeforeItAll).Outcome
            .Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
    }

    [Fact]
    public void CheckBlockDelete_AFutureFreeBonusBlock_Allowed_OnceStarted_Never()
    {
        var contract = Contract(StrictnessRule.Locked, bonusBlocksFollowStrictness: false);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        contract.CheckBlockDelete(block, BeforeItAll).Outcome.Should().Be(ChangeOutcome.Allowed);
        contract.CheckBlockDelete(block, At.Utc(2026, 1, 6, 11)).Outcome.Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
        contract.CheckBlockDelete(block, At.Utc(2026, 1, 6, 13)).Outcome.Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
    }

    [Fact]
    public void CheckBlockDelete_ABindingBonusBlock_Never()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, bonusBlocksFollowStrictness: true);

        contract.CheckBlockDelete(Bonus(contract, Slot(Tuesday, 10, 12)), BeforeItAll).Outcome
            .Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
    }

    // ── A finished contract: its blocks are history ──────────────────────────

    [Fact]
    public void AFinishedContract_RefusesEveryChange_AndEveryDelete()
    {
        var contract = Finished(StrictnessRule.PeriodTimeBudget);
        var committed = Committed(contract, Slot(Monday, 10, 12));
        var ownBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Slot(Monday, 13, 14),
            membership: TestContracts.Bonus(contract.Id));

        contract.CheckBlockMove(committed, Slot(Monday, 14, 16), At.UtcZone).Outcome.Should().Be(ChangeOutcome.ContractIsOver);
        contract.CheckBlockPresetChange(ownBonus).Outcome.Should().Be(ChangeOutcome.ContractIsOver);
        contract.CheckBlockSplit(committed).Outcome.Should().Be(ChangeOutcome.ContractIsOver);
        contract.CheckBlockDelete(ownBonus, BeforeItAll).Outcome.Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
    }

    // ── Floor ────────────────────────────────────────────────────────────────

    private static Dictionary<DateOnly, double> Plan(double monday, double tuesday, double wednesday) =>
        new() { [Monday] = monday, [Tuesday] = tuesday, [Wednesday] = wednesday };

    [Fact]
    public void CheckFloor_EveryDayAtOrAboveItsFloor_Allowed()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: Plan(2, 2, 2));

        contract.CheckFloor(Plan(2, 3, 2)).Should().Be(ChangeVerdict.Allowed);
    }

    [Fact]
    public void CheckFloor_ADayBelowItsFloor_NamesTheDayAndTheFloor()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: Plan(2, 2, 2));

        contract.CheckFloor(Plan(2, 1.5, 2)).Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    [Fact]
    public void CheckFloor_ChecksEveryDay_NotOnlyTheOnesAChangeTouched()
    {
        // The same answer whichever day the change was on — there is no list of touched days.
        var contract = Contract(StrictnessRule.PeriodTimeBudget, fixedFloorHours: 2, plan: Plan(2, 2, 2));

        contract.CheckFloor(Plan(4, 2, 0)).Should().Be(ChangeVerdict.BelowDailyFloor(Wednesday, 2));
    }

    [Fact]
    public void CheckFloor_ADayMissingFromThePlan_CountsAsNothingPlanned()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: Plan(2, 2, 2));

        contract.CheckFloor(new Dictionary<DateOnly, double> { [Monday] = 2, [Wednesday] = 2 })
            .Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    [Fact]
    public void CheckFloor_FloorsThatFollowThePlan_ProtectWhatWasPlanned()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, plan: Plan(3, 0, 1));

        contract.CheckFloor(Plan(3, 0, 1)).Should().Be(ChangeVerdict.Allowed);
        contract.CheckFloor(Plan(2.5, 0, 1)).Should().Be(ChangeVerdict.BelowDailyFloor(Monday, 3));
    }

    [Fact]
    public void CheckFloor_WithAWholePeriodTarget_ChecksTheTotal_NotTheDays()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, plan: new Dictionary<DateOnly, double> { [Monday] = 6 }); // the target is the 6h signed

        contract.CheckFloor(Plan(6, 0, 0)).Should().Be(ChangeVerdict.Allowed);
        contract.CheckFloor(Plan(3, 2, 0.5)).Should().Be(ChangeVerdict.BelowPeriodTarget(6));
    }
}
