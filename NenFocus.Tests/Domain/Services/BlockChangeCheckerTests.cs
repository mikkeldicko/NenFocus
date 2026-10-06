using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.Services;

/// <summary>
/// Gap 93: the one answer to "may this block be changed or deleted". The checker holds no rule —
/// it asks the block, then the block's contract, first refusal wins — so these tests are about
/// the order, what is handed to the contract, and the flags. The rules themselves are tested on
/// PlannedBlock and NenContract. Monday 5 to Wednesday 7 January 2026, Work Hours 09:00–17:00, UTC.
/// </summary>
public class BlockChangeCheckerTests
{
    private static readonly DateOnly Monday = new(2026, 1, 5);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly WorkHours NineToFive = new(new TimeOnly(9, 0), new TimeOnly(17, 0));
    private static readonly Moment BeforeItAll = At.Utc(2026, 1, 4, 13);

    private static readonly BlockChangeChecker Checker = new(new PlannedFocusHoursCalculator());

    private static NenContract Contract(StrictnessRule strictness, bool bonusBlocksFollowStrictness = false,
        double? fixedFloorHours = null, IReadOnlyDictionary<DateOnly, double>? plan = null)
    {
        var rules = TestRules.Of(strictness, fixedFloorHours is { } hours ? new DailyFloor(hours) : null,
            bonusBlocksFollowStrictness: bonusBlocksFollowStrictness);
        plan ??= new Dictionary<DateOnly, double>();
        return NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(Monday, Wednesday, rules.FloorRule, plan, NineToFive), null, plan,
            At.Utc(2026, 1, 4, 12), At.UtcZone);
    }

    private static Dictionary<DateOnly, double> TwoHoursEveryDay => new() { [Monday] = 2, [Tuesday] = 2, [Wednesday] = 2 };

    private static TimeSlot Slot(DateOnly day, double fromHour, double toHour) =>
        At.Slot(day.ToDateTime(TimeOnly.MinValue).AddHours(fromHour), day.ToDateTime(TimeOnly.MinValue).AddHours(toHour));

    private static FocusBlock Plain(TimeSlot slot) => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot);

    private static FocusBlock Committed(NenContract contract, TimeSlot slot) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot, membership: TestContracts.Committed(contract.Id));

    private static FocusBlock Bonus(NenContract contract, TimeSlot slot) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot, membership: contract.MembershipForPlacedBlock(claimsDeficit: false));

    private static ChangeVerdict Reschedule(PlannedBlock block, TimeSlot newSlot, NenContract? contract,
        IEnumerable<PlannedBlock>? contractBlocks = null, bool presetChanges = false, Moment? asOf = null) =>
        Checker.CheckReschedule(block, newSlot, presetChanges, contract, contractBlocks ?? [block], asOf ?? BeforeItAll, At.UtcZone);

    // ── A block in no contract: only its own lock ────────────────────────────

    [Fact]
    public void APlainBlock_ThatHasNotStarted_MayBeMovedReSetAndDeleted_ButNotSplit()
    {
        var block = Plain(Slot(Tuesday, 10, 12));

        Reschedule(block, Slot(Wednesday, 20, 22), contract: null, presetChanges: true).Should().Be(ChangeVerdict.Allowed);
        Checker.CheckDelete(block, null, BeforeItAll).Should().Be(ChangeVerdict.Allowed);
        Checker.CheckSplit(block, Slot(Tuesday, 10, 11), Slot(Tuesday, 13, 14), null, [], BeforeItAll, At.UtcZone)
            .Outcome.Should().Be(ChangeOutcome.SplitNotOffered);
    }

    [Fact]
    public void APlainBlock_OnceStarted_IsLocked_AndWhileRunningCannotBeDeleted()
    {
        var block = Plain(Slot(Tuesday, 10, 12));
        var running = At.Utc(2026, 1, 6, 11);
        var ended = At.Utc(2026, 1, 6, 12);

        Reschedule(block, Slot(Tuesday, 13, 15), null, asOf: running).Outcome.Should().Be(ChangeOutcome.BlockHasStarted);
        Checker.CheckDelete(block, null, running).Outcome.Should().Be(ChangeOutcome.BlockIsRunning);
        Reschedule(block, Slot(Tuesday, 13, 15), null, asOf: ended).Outcome.Should().Be(ChangeOutcome.BlockHasStarted);
        Checker.CheckDelete(block, null, ended).Should().Be(ChangeVerdict.Allowed);
    }

    // ── A contract's block: the block first, then the contract ───────────────

    [Fact]
    public void TheBlocksOwnLock_IsAskedBeforeTheContract()
    {
        var contract = Contract(StrictnessRule.Locked);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Reschedule(block, Slot(Tuesday, 13, 15), contract, asOf: At.Utc(2026, 1, 6, 11))
            .Outcome.Should().Be(ChangeOutcome.BlockHasStarted);
        Checker.CheckDelete(block, contract, At.Utc(2026, 1, 6, 11)).Outcome.Should().Be(ChangeOutcome.BlockIsRunning);
    }

    [Fact]
    public void AMemberWhoseContractIsGone_IsHistory()
    {
        var block = Committed(Contract(StrictnessRule.PeriodTimeBudget), Slot(Tuesday, 10, 12));

        Reschedule(block, Slot(Tuesday, 13, 15), contract: null).Outcome.Should().Be(ChangeOutcome.ContractIsOver);
        Checker.CheckDelete(block, null, BeforeItAll).Outcome.Should().Be(ChangeOutcome.ContractBlockNeverDeleted);
        Checker.AllowancesFor(block, null, BeforeItAll).Should().Be(new BlockChangeAllowances(false, false, false, false));
    }

    [Fact]
    public void CheckReschedule_AsksTheContractAboutTheMove()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Reschedule(block, Slot(Tuesday, 14, 16), contract).Should().Be(ChangeVerdict.Allowed);
        Reschedule(block, Slot(Wednesday, 10, 12), contract).Outcome.Should().Be(ChangeOutcome.OutsideOwnDay);
    }

    [Fact]
    public void CheckReschedule_APresetChange_IsAskedOfTheContract()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Reschedule(block, block.Scheduled, contract, presetChanges: true).Outcome.Should().Be(ChangeOutcome.PresetFixed);
    }

    [Fact]
    public void CheckReschedule_WithNothingChanged_IsAllowed_EvenUnderLockdown()
    {
        // Saving the form untouched is not a move: the contract is only asked about what changes.
        var contract = Contract(StrictnessRule.Locked);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Reschedule(block, block.Scheduled, contract).Should().Be(ChangeVerdict.Allowed);
    }

    [Fact]
    public void CheckReschedule_ShrinkingBelowTheDaysFloor_NamesTheDay()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: TwoHoursEveryDay);
        var monday = Committed(contract, Slot(Monday, 10, 12));
        var tuesday = Committed(contract, Slot(Tuesday, 10, 12));
        var wednesday = Committed(contract, Slot(Wednesday, 10, 12));

        Reschedule(tuesday, Slot(Tuesday, 10, 11.5), contract, [monday, tuesday, wednesday])
            .Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    [Fact]
    public void CheckReschedule_MovingHoursToAnotherDay_ChecksTheDayLeftBehind()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, fixedFloorHours: 2, plan: TwoHoursEveryDay);
        var monday = Committed(contract, Slot(Monday, 10, 12));
        var tuesday = Committed(contract, Slot(Tuesday, 10, 12));
        var wednesday = Committed(contract, Slot(Wednesday, 10, 12));

        Reschedule(tuesday, Slot(Wednesday, 13, 15), contract, [monday, tuesday, wednesday])
            .Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    [Fact]
    public void CheckReschedule_TheFloorCountsOnlyBlocksThatCountTowardIt()
    {
        // A Bonus block's two hours on Tuesday don't make up for the committed block shrinking.
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: TwoHoursEveryDay);
        var monday = Committed(contract, Slot(Monday, 10, 12));
        var tuesday = Committed(contract, Slot(Tuesday, 10, 12));
        var wednesday = Committed(contract, Slot(Wednesday, 10, 12));
        var bonus = Bonus(contract, Slot(Tuesday, 13, 15));

        Reschedule(tuesday, Slot(Tuesday, 10, 11), contract, [monday, tuesday, wednesday, bonus])
            .Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    // ── Floors are minimums of what was signed; a Redemption block's length is fixed (Gap 95, decision 16) ──

    private static FocusBlock Redemption(NenContract contract, TimeSlot slot) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), slot, membership: TestContracts.Redemption(contract.Id));

    [Fact]
    public void CheckReschedule_ARedemptionBlock_DoesNotMakeUpForAShrinkingCommittedBlock()
    {
        // Tuesday: 2h committed + 1h redeeming a missed block. The 2h signed for Tuesday stays
        // owed by the committed block — the redemption pays for something else.
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: TwoHoursEveryDay);
        var monday = Committed(contract, Slot(Monday, 10, 12));
        var tuesday = Committed(contract, Slot(Tuesday, 10, 12));
        var wednesday = Committed(contract, Slot(Wednesday, 10, 12));
        var redemption = Redemption(contract, Slot(Tuesday, 13, 14));

        Reschedule(tuesday, Slot(Tuesday, 10, 11), contract, [monday, tuesday, wednesday, redemption])
            .Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    [Theory]
    [InlineData(13, 13.5)] // shorter
    [InlineData(13, 15)]   // longer
    public void CheckReschedule_ResizingARedemptionBlock_IsRefused(double fromHour, double toHour)
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, plan: TwoHoursEveryDay);
        var redemption = Redemption(contract, Slot(Tuesday, 13, 14));

        Reschedule(redemption, Slot(Tuesday, fromHour, toHour), contract)
            .Should().Be(ChangeVerdict.Refused(ChangeOutcome.RedemptionLengthFixed));
    }

    [Fact]
    public void CheckReschedule_MovingARedemptionBlockAtTheSameLength_IsAllowed()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, plan: TwoHoursEveryDay);
        var redemption = Redemption(contract, Slot(Tuesday, 13, 14));

        Reschedule(redemption, Slot(Tuesday, 15, 16), contract).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CheckSplit_ARedemptionBlock_MayBeSplit_ThePiecesKeepItsLength()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, plan: TwoHoursEveryDay);
        var redemption = Redemption(contract, Slot(Tuesday, 13, 15));

        Checker.CheckSplit(redemption, Slot(Tuesday, 13, 14), Slot(Tuesday, 14, 15), contract, [redemption], BeforeItAll, At.UtcZone)
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CheckReschedule_ABonusBlock_IsNeverHeldToTheFloor()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility, fixedFloorHours: 2, plan: TwoHoursEveryDay);
        var bonus = Bonus(contract, Slot(Tuesday, 13, 15));

        // No committed block is handed in at all — the floor would fail if it were asked.
        Reschedule(bonus, Slot(Tuesday, 13, 14), contract, [bonus]).Should().Be(ChangeVerdict.Allowed);
    }

    [Fact]
    public void CheckReschedule_AFreeBonusBlock_StillCannotLeaveTheContractsPeriod()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var bonus = Bonus(contract, Slot(Tuesday, 13, 15));

        Reschedule(bonus, Slot(Wednesday.AddDays(1), 13, 15), contract).Outcome.Should().Be(ChangeOutcome.OutsideContractPeriod);
    }

    // ── Split ────────────────────────────────────────────────────────────────

    private static ChangeVerdict Split(PlannedBlock block, TimeSlot first, TimeSlot second, NenContract? contract,
        IEnumerable<PlannedBlock>? contractBlocks = null) =>
        Checker.CheckSplit(block, first, second, contract, contractBlocks ?? [block], BeforeItAll, At.UtcZone);

    [Fact]
    public void CheckSplit_UnderDailyFlex_BothHalvesOnTheBlocksOwnDay_Allowed()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Split(block, Slot(Tuesday, 9, 10), Slot(Tuesday, 15, 16), contract).Should().Be(ChangeVerdict.Allowed);
    }

    [Fact]
    public void CheckSplit_UnderDailyFlex_AHalfOnAnotherDay_OutsideOwnDay()
    {
        var contract = Contract(StrictnessRule.DailyFlexibility);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Split(block, Slot(Tuesday, 9, 10), Slot(Wednesday, 15, 16), contract).Outcome.Should().Be(ChangeOutcome.OutsideOwnDay);
    }

    [Fact]
    public void CheckSplit_UnderPeriodTimeBudget_AHalfMayLandOnAnotherDayOfTheContract()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Split(block, Slot(Tuesday, 9, 10), Slot(Wednesday, 15, 16), contract).Should().Be(ChangeVerdict.Allowed);
    }

    [Fact]
    public void CheckSplit_UnderLockdown_NoMovementAllowed()
    {
        var contract = Contract(StrictnessRule.Locked);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Split(block, Slot(Tuesday, 9, 10), Slot(Tuesday, 15, 16), contract).Outcome.Should().Be(ChangeOutcome.NoMovementAllowed);
    }

    [Fact]
    public void CheckSplit_AFreeBonusBlock_SplitNotOffered()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        Split(block, Slot(Tuesday, 9, 10), Slot(Tuesday, 15, 16), contract).Outcome.Should().Be(ChangeOutcome.SplitNotOffered);
    }

    [Fact]
    public void CheckSplit_MovingAHalfToAnotherDay_ChecksTheDayLeftShort()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, fixedFloorHours: 2, plan: TwoHoursEveryDay);
        var monday = Committed(contract, Slot(Monday, 10, 12));
        var tuesday = Committed(contract, Slot(Tuesday, 10, 12));
        var wednesday = Committed(contract, Slot(Wednesday, 10, 12));

        Split(tuesday, Slot(Tuesday, 10, 11), Slot(Wednesday, 13, 14), contract, [monday, tuesday, wednesday])
            .Should().Be(ChangeVerdict.BelowDailyFloor(Tuesday, 2));
    }

    // ── The flags ────────────────────────────────────────────────────────────

    [Fact]
    public void AllowancesFor_APlainBlock_FollowItsOwnLock()
    {
        var block = Plain(Slot(Tuesday, 10, 12));

        Checker.AllowancesFor(block, null, BeforeItAll)
            .Should().Be(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: true, CanSplit: false, CanDelete: true));
        Checker.AllowancesFor(block, null, At.Utc(2026, 1, 6, 11))
            .Should().Be(new BlockChangeAllowances(false, false, false, false));
        Checker.AllowancesFor(block, null, At.Utc(2026, 1, 6, 12))
            .Should().Be(new BlockChangeAllowances(false, false, false, CanDelete: true));
    }

    [Theory]
    [InlineData(StrictnessRule.Locked, false, false)]
    [InlineData(StrictnessRule.DailyFlexibility, true, true)]
    [InlineData(StrictnessRule.PeriodTimeBudget, true, true)]
    public void AllowancesFor_ACommittedBlock_ByStrictness(StrictnessRule strictness, bool canReschedule, bool canSplit)
    {
        var contract = Contract(strictness);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Checker.AllowancesFor(block, contract, BeforeItAll)
            .Should().Be(new BlockChangeAllowances(canReschedule, CanChangePreset: false, canSplit, CanDelete: false)
            {
                SplitPiecesMayChangeDay = strictness == StrictnessRule.PeriodTimeBudget
            });
    }

    [Theory]
    [InlineData(StrictnessRule.Locked, false)]
    [InlineData(StrictnessRule.DailyFlexibility, false)]
    [InlineData(StrictnessRule.PeriodTimeBudget, true)]
    public void AllowancesFor_SplitPiecesMayChangeDay_OnlyWhereTheBlockMayMoveToAnotherDay(StrictnessRule strictness, bool expected)
    {
        // What tells a screen whether a split needs a day per piece at all.
        var contract = Contract(strictness);
        var block = Committed(contract, Slot(Tuesday, 10, 12));

        Checker.AllowancesFor(block, contract, BeforeItAll).SplitPiecesMayChangeDay.Should().Be(expected);
    }

    [Fact]
    public void AllowancesFor_ABlockThatCannotBeSplit_HasNoPiecesToMove()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);

        Checker.AllowancesFor(Bonus(contract, Slot(Tuesday, 10, 12)), contract, BeforeItAll).SplitPiecesMayChangeDay.Should().BeFalse();
        Checker.AllowancesFor(Plain(Slot(Tuesday, 10, 12)), null, BeforeItAll).SplitPiecesMayChangeDay.Should().BeFalse();
    }

    [Fact]
    public void AllowancesFor_AFreeBonusBlock_AreAPlainBlocks_EvenUnderLockdown()
    {
        var contract = Contract(StrictnessRule.Locked, bonusBlocksFollowStrictness: false);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        Checker.AllowancesFor(block, contract, BeforeItAll)
            .Should().Be(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: true, CanSplit: false, CanDelete: true));
    }

    [Fact]
    public void AllowancesFor_ABindingBonusBlock_AreACommittedBlocks()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget, bonusBlocksFollowStrictness: true);
        var block = Bonus(contract, Slot(Tuesday, 10, 12));

        Checker.AllowancesFor(block, contract, BeforeItAll)
            .Should().Be(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: false, CanSplit: true, CanDelete: false)
            {
                SplitPiecesMayChangeDay = true
            });
    }

    [Fact]
    public void AllowancesFor_ABlockOfAFinishedContract_AllowNothing()
    {
        var contract = Contract(StrictnessRule.PeriodTimeBudget);
        var block = Committed(contract, Slot(Monday, 10, 12));
        contract.Break(At.Utc(2026, 1, 4, 14), At.UtcZone);

        Checker.AllowancesFor(block, contract, BeforeItAll).Should().Be(new BlockChangeAllowances(false, false, false, false));
    }
}
