using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class NenContractTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7); // 7-day range
    private static readonly DateTime SealedBeforeStart = new(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SealedOnStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Dictionary<DateOnly, double> FullyPlanned(double hoursPerDay = 4)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
            map[date] = hoursPerDay;
        return map;
    }

    private static readonly WorkHours NineToFive = new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    /// <param name="floorPlan">The hours a FollowsPlan rule turns into floors — defaults to plannedFocusHoursByDay.</param>
    /// <param name="calendar">Explicit days, for tests about per-day windows no stencil can produce.</param>
    private static NenContract Seal(
        DailyFloorRule? floorRule = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        DateTime? sealedAt = null,
        bool allowCancellation = false,
        ChangeWindow? changeWindow = null,
        StrictnessRule strictnessRule = StrictnessRule.Locked,
        WorkHours? workHours = null,
        IReadOnlyCollection<DateOnly>? restDays = null,
        IReadOnlyCollection<DateOnly>? noWorkHoursDays = null,
        IReadOnlyDictionary<DateOnly, double>? floorPlan = null,
        ContractCalendar? calendar = null,
        Guid? contractPresetId = null,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract)
    {
        var rule = floorRule ?? new DailyFloorRule.Fixed(new DailyFloor(4));
        var plan = plannedFocusHoursByDay ?? FullyPlanned();
        return TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            strictnessRule: strictnessRule,
            calendar: calendar ?? TestCalendars.For(StartDate, EndDate, rule, floorPlan ?? plan, workHours, restDays, noWorkHoursDays),
            floorRule: rule,
            changeWindow: changeWindow,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plan,
            sealedAt: At.Utc(sealedAt ?? SealedOnStart), timeZone: At.UtcZone,
            contractPresetId: contractPresetId,
            redemptionPlacementPolicy: redemptionPlacementPolicy);
    }

    /// <summary>Full days from StartDate to EndDate, no floors, except the given windows.</summary>
    private static ContractCalendar DaysWithWindows(Dictionary<DateOnly, (DateTime Start, DateTime End)> windows)
    {
        var days = new List<ContractDay>();
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var (start, end) = windows.TryGetValue(date, out var w)
                ? w
                : (date.ToDateTime(TimeOnly.MinValue), date.AddDays(1).ToDateTime(TimeOnly.MinValue));
            days.Add(new ContractDay(date, start, end, null));
        }
        return new ContractCalendar(days);
    }

    [Fact]
    public void Seal_EndBeforeStart_Throws()
    {
        var act = () => TestCalendars.For(EndDate, StartDate, new DailyFloorRule.None());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_BlankName_Throws()
    {
        var act = () => TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "   ",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: FullyPlanned(),
            sealedAt: At.Utc(SealedOnStart), timeZone: At.UtcZone);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_EveryDayMeetsFloor_Succeeds()
    {
        var act = () => Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), plannedFocusHoursByDay: FullyPlanned(4));

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_OneDayBelowFloor_Throws()
    {
        var plan = FullyPlanned(4);
        plan[StartDate.AddDays(3)] = 2; // below the floor of 4

        var act = () => Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), plannedFocusHoursByDay: plan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_DayMissingFromMap_TreatedAsZeroAndThrows()
    {
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(2));

        var act = () => Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), plannedFocusHoursByDay: plan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_FloorDisabled_NoPlannedHoursRequired()
    {
        var act = () => Seal(floorRule: new DailyFloorRule.None(), plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_RestDayViaPerDayException_SkippedEvenWithNoPlannedHours()
    {
        // Default floor of 4h applies every day except the explicit rest-day exception.
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(5)); // no hours planned on the rest day

        var act = () => Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [StartDate.AddDays(5)], plannedFocusHoursByDay: plan);

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_SimpleContractStyle_PerDayFloorVariesWithPlannedHours()
    {
        // Simple contracts: no Default, every planned day gets its own floor equal to
        // that day's planned hours — so the check always passes by construction.
        var plan = new Dictionary<DateOnly, double>
        {
            [StartDate] = 2,
            [StartDate.AddDays(1)] = 3
            // remaining days omitted — no floor, no plan, true rest days
        };
        var act = () => Seal(floorRule: new DailyFloorRule.FollowsPlan(), plannedFocusHoursByDay: plan);

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_SealedBeforeStartDate_StatusIsScheduled()
    {
        var contract = Seal(sealedAt: SealedBeforeStart);

        contract.Status.Should().Be(NenContractStatus.Scheduled);
    }

    [Fact]
    public void Seal_SealedOnOrAfterStartDate_StatusIsActive()
    {
        var contract = Seal(sealedAt: SealedOnStart);

        contract.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public void Seal_RaisesNenContractSealedEvent()
    {
        var contract = Seal();

        contract.DomainEvents.Should().ContainSingle(e => e is NenContractSealedEvent);
    }

    [Fact]
    public void Activate_WhenScheduled_TransitionsToActiveAndRaisesEvent()
    {
        var contract = Seal(sealedAt: SealedBeforeStart);
        contract.ClearDomainEvents();

        contract.Activate(At.Utc(SealedOnStart));

        contract.Status.Should().Be(NenContractStatus.Active);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractActivatedEvent);
    }

    [Fact]
    public void Activate_WhenNotScheduled_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart); // already Active

        var act = () => contract.Activate(At.Utc(SealedOnStart));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_WhenActive_TransitionsToCompletedAndRaisesEvent()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.ClearDomainEvents();

        contract.Complete(At.Utc(SealedOnStart.AddDays(7)));

        contract.Status.Should().Be(NenContractStatus.Completed);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractCompletedEvent);
    }

    [Fact]
    public void Complete_WhenScheduled_Throws()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        var act = () => contract.Complete(At.Utc(SealedOnStart));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(At.Utc(SealedOnStart.AddDays(7)));

        var act = () => contract.Complete(At.Utc(SealedOnStart.AddDays(8)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Break_WhenActiveAndCancellationAllowed_TransitionsToBrokenAndRaisesEvent()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.ClearDomainEvents();

        contract.Break(At.Utc(SealedOnStart.AddDays(2)), At.UtcZone);

        contract.Status.Should().Be(NenContractStatus.Broken);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractBrokenEvent);
    }

    [Fact]
    public void Break_WhenCancellationNotAllowed_StillSucceeds_InterimUntilStrictModeExists()
    {
        // AllowCancellation is deliberately not enforced by Break right now — see the
        // comment on Break itself. This will change once Strict Mode is the real gate.
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: false);
        contract.ClearDomainEvents();

        contract.Break(At.Utc(SealedOnStart.AddDays(2)), At.UtcZone);

        contract.Status.Should().Be(NenContractStatus.Broken);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractBrokenEvent);
    }

    [Fact]
    public void Break_WhenScheduled_TransitionsToBrokenAndRaisesEvent()
    {
        // A not-yet-started contract is still "in effect" and can be cancelled before Day 1.
        var contract = Seal(sealedAt: SealedBeforeStart, allowCancellation: true); // Scheduled
        contract.ClearDomainEvents();

        contract.Break(At.Utc(SealedOnStart), At.UtcZone);

        contract.Status.Should().Be(NenContractStatus.Broken);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractBrokenEvent);
    }

    [Fact]
    public void Break_WhenAlreadyCompleted_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true); // Active
        contract.Complete(At.Utc(SealedOnStart.AddDays(8)));

        var act = () => contract.Break(At.Utc(SealedOnStart.AddDays(9)), At.UtcZone);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OriginalEndDate_SetAtSeal_EqualsEndDate()
    {
        var contract = Seal();

        contract.OriginalEndDate.Should().Be(EndDate);
    }

    [Fact]
    public void Break_WhileActive_ShrinksEndDateToTheBreakDate()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        var breakDate = StartDate.AddDays(2);

        contract.Break(At.Utc(breakDate.ToDateTime(new TimeOnly(14, 0))), At.UtcZone);

        contract.EndDate.Should().Be(breakDate);
    }

    [Fact]
    public void Break_WhileActive_PreservesOriginalEndDate()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);

        contract.Break(At.Utc(StartDate.AddDays(2).ToDateTime(new TimeOnly(14, 0))), At.UtcZone);

        contract.OriginalEndDate.Should().Be(EndDate);
    }

    [Fact]
    public void Break_BeforeContractEverStarted_ClampsEndDateToStartDate()
    {
        // A Scheduled contract broken before Day 1 ever arrives — EndDate must not end up
        // before StartDate (would violate Seal's own invariant), so it clamps to StartDate
        // rather than to the literal break date.
        var contract = Seal(sealedAt: SealedBeforeStart, allowCancellation: true); // Scheduled

        contract.Break(At.Utc(SealedBeforeStart), At.UtcZone);

        contract.EndDate.Should().Be(StartDate);
        contract.OriginalEndDate.Should().Be(EndDate); // OriginalEndDate is never touched by Break
    }

    [Fact]
    public void WindowOn_WithoutWorkHours_IsTheWholeDay()
    {
        var contract = Seal();

        contract.WindowOn(StartDate.AddDays(1)).Should().Be((
            StartDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            StartDate.AddDays(2).ToDateTime(TimeOnly.MinValue)));
    }

    [Fact]
    public void WindowOn_WithWorkHours_IsThatDaysWorkHours()
    {
        var contract = Seal(workHours: NineToFive);

        contract.WindowOn(StartDate.AddDays(1)).Should().Be((
            StartDate.AddDays(1).ToDateTime(new TimeOnly(9, 0)),
            StartDate.AddDays(1).ToDateTime(new TimeOnly(17, 0))));
    }

    [Fact]
    public void WindowOn_OutsideTheContract_IsNull()
    {
        var contract = Seal();

        contract.WindowOn(EndDate.AddDays(1)).Should().BeNull();
    }

    [Fact]
    public void FloorRule_IsKeptAsSealed()
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)));

        contract.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(4)));
    }

    [Fact]
    public void IsDueForActivation_ScheduledAndItsStartMomentArrived_True()
    {
        var contract = Seal(sealedAt: SealedBeforeStart, workHours: NineToFive); // Scheduled, starts 09:00

        contract.IsDueForActivation(StartDate.ToDateTime(new TimeOnly(9, 0))).Should().BeTrue();
    }

    [Fact]
    public void IsDueForActivation_OnTheStartDayButBeforeItsStartMoment_False()
    {
        var contract = Seal(sealedAt: SealedBeforeStart, workHours: NineToFive); // starts 09:00

        contract.IsDueForActivation(StartDate.ToDateTime(new TimeOnly(8, 59))).Should().BeFalse();
    }

    [Fact]
    public void IsDueForActivation_ScheduledButStartDateNotYetArrived_False()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        contract.IsDueForActivation(StartDate.AddDays(-1).ToDateTime(new TimeOnly(23, 59))).Should().BeFalse();
    }

    [Fact]
    public void IsDueForActivation_AlreadyActive_False()
    {
        var contract = Seal(sealedAt: SealedOnStart); // Active

        contract.IsDueForActivation(StartDate.ToDateTime(new TimeOnly(12, 0))).Should().BeFalse();
    }

    [Fact]
    public void IsDueForCompletion_ActiveAndItsEndMomentArrived_True()
    {
        var contract = Seal(sealedAt: StartDate.ToDateTime(new TimeOnly(9, 0)), workHours: NineToFive); // Active; ends 17:00 on EndDate

        contract.IsDueForCompletion(EndDate.ToDateTime(new TimeOnly(17, 0))).Should().BeTrue();
    }

    [Fact]
    public void IsDueForCompletion_OnTheLastDayBeforeItsEndMoment_False()
    {
        var contract = Seal(sealedAt: SealedOnStart); // Active, runs to midnight after EndDate

        contract.IsDueForCompletion(EndDate.ToDateTime(new TimeOnly(23, 59))).Should().BeFalse();
    }

    [Fact]
    public void IsDueForCompletion_StillScheduled_False()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        contract.IsDueForCompletion(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue)).Should().BeFalse();
    }

    [Fact]
    public void Governs_IsTouchingTheHalfOpenPeriodFromStartsAtToEndsAt()
    {
        var contract = Seal(workHours: NineToFive);
        TimeSlot Minute(DateOnly date, int hour, int minute) =>
            At.Slot(date.ToDateTime(new TimeOnly(hour, minute)), date.ToDateTime(new TimeOnly(hour, minute)).AddMinutes(1));

        contract.Governs(Minute(StartDate, 8, 59), At.UtcZone).Should().BeFalse();
        contract.Governs(Minute(StartDate, 9, 0), At.UtcZone).Should().BeTrue();
        contract.Governs(Minute(EndDate, 16, 59), At.UtcZone).Should().BeTrue();
        contract.Governs(Minute(EndDate, 17, 0), At.UtcZone).Should().BeFalse();
    }

    [Fact]
    public void Governs_AFinishedContract_GovernsNothing()
    {
        var contract = Seal(workHours: NineToFive);
        var inside = At.Slot(StartDate.ToDateTime(new TimeOnly(10, 0)), StartDate.ToDateTime(new TimeOnly(11, 0)));
        contract.Governs(inside, At.UtcZone).Should().BeTrue();

        contract.Break(At.Utc(StartDate.ToDateTime(new TimeOnly(9, 30))), At.UtcZone);

        contract.Governs(inside, At.UtcZone).Should().BeFalse();
    }

    // ── What breaking does to a block (Gap 97) ───────────────────────────────

    private static readonly DateTime BrokenAt = StartDate.ToDateTime(new TimeOnly(12, 0));

    private static FocusBlock BlockAt(int fromHour, int toHour, ContractMembership? membership) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            At.Slot(StartDate.ToDateTime(new TimeOnly(fromHour, 0)), StartDate.ToDateTime(new TimeOnly(toHour, 0))), membership: membership);

    [Theory]
    [InlineData(14, 15, BlockFateOnBreak.Cancel)] // upcoming: not final
    [InlineData(9, 10, BlockFateOnBreak.Leave)]   // missed: final
    public void FateWhenBroken_CommittedOrRedemptionBlock_CancelledUnlessFinal_WhateverTheBonusChoice(int fromHour, int toHour, BlockFateOnBreak expected)
    {
        var contract = Seal();
        var committed = BlockAt(fromHour, toHour, TestContracts.Committed(contract.Id));
        var redemption = BlockAt(fromHour, toHour, TestContracts.Redemption(contract.Id));

        foreach (var keepBonus in new[] { true, false })
        {
            contract.FateWhenBroken(committed, At.Utc(BrokenAt), keepBonus).Should().Be(expected);
            contract.FateWhenBroken(redemption, At.Utc(BrokenAt), keepBonus).Should().Be(expected);
        }
    }

    [Fact]
    public void FateWhenBroken_CommittedBlockWhoseSessionEndedEarly_IsLeft()
    {
        // Its session already ended: the block is final, so breaking never relabels it Cancelled.
        var contract = Seal();
        var endedEarly = TestBlocks.Worked(BlockAt(11, 13, TestContracts.Committed(contract.Id)), ritualAfterStart: 0, endedAfterStart: 30);

        contract.FateWhenBroken(endedEarly, At.Utc(BrokenAt), keepBonusBlocks: true).Should().Be(BlockFateOnBreak.Leave);
    }

    [Theory]
    [InlineData(true, BlockFateOnBreak.Release)]
    [InlineData(false, BlockFateOnBreak.Cancel)]
    public void FateWhenBroken_BonusBlockNotYetStarted_FollowsTheUsersChoice(bool keepBonusBlocks, BlockFateOnBreak expected)
    {
        var contract = Seal();
        var upcoming = BlockAt(14, 15, TestContracts.Bonus(contract.Id));

        contract.FateWhenBroken(upcoming, At.Utc(BrokenAt), keepBonusBlocks).Should().Be(expected);
    }

    [Fact]
    public void FateWhenBroken_BonusBlockStartedOrFinished_IsLeft_WhateverTheChoice()
    {
        var contract = Seal();
        var running = BlockAt(11, 13, TestContracts.Bonus(contract.Id));
        var startingNow = BlockAt(12, 13, TestContracts.Bonus(contract.Id));
        var finished = BlockAt(9, 10, TestContracts.Bonus(contract.Id));

        foreach (var keepBonus in new[] { true, false })
        {
            contract.FateWhenBroken(running, At.Utc(BrokenAt), keepBonus).Should().Be(BlockFateOnBreak.Leave);
            contract.FateWhenBroken(startingNow, At.Utc(BrokenAt), keepBonus).Should().Be(BlockFateOnBreak.Leave);
            contract.FateWhenBroken(finished, At.Utc(BrokenAt), keepBonus).Should().Be(BlockFateOnBreak.Leave);
        }
    }

    [Fact]
    public void FateWhenBroken_BlockOfAnotherContractOrOfNone_Throws()
    {
        var contract = Seal();
        var foreign = () => contract.FateWhenBroken(BlockAt(14, 15, TestContracts.Committed(Guid.NewGuid())), At.Utc(BrokenAt), true);
        var plain = () => contract.FateWhenBroken(BlockAt(14, 15, null), At.Utc(BrokenAt), true);

        foreign.Should().Throw<ArgumentException>();
        plain.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MembershipForPlacedBlock_AFinishedContract_Throws()
    {
        var contract = Seal(workHours: NineToFive);
        contract.Break(At.Utc(StartDate.ToDateTime(new TimeOnly(9, 30))), At.UtcZone);

        var act = () => contract.MembershipForPlacedBlock();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Seal_StartMomentAlreadyPassed_Throws()
    {
        // Contract starts 09:00 on StartDate; sealing at 09:01 is too late.
        var act = () => Seal(workHours: NineToFive, sealedAt: StartDate.ToDateTime(new TimeOnly(9, 1)));

        act.Should().Throw<ArgumentException>().WithMessage("*start time has passed*");
    }

    [Fact]
    public void Seal_WithinTheStartMinute_IsActive()
    {
        // "Right now" is measured to the minute: sealing at 09:00:40 for a 09:00 start is allowed.
        var contract = Seal(workHours: NineToFive, sealedAt: StartDate.ToDateTime(new TimeOnly(9, 0, 40)));

        contract.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public void Seal_BeforeTheStartMomentOnTheStartDay_IsScheduled()
    {
        var contract = Seal(workHours: NineToFive, sealedAt: StartDate.ToDateTime(new TimeOnly(8, 0)));

        contract.Status.Should().Be(NenContractStatus.Scheduled);
    }

    [Fact]
    public void Seal_FloorRuleNoneButADayHasAFloor_Throws()
    {
        // "No daily floors" only exists under Period Time Budget with no fixed floor (Gap 93).
        var act = () => Seal(
            strictnessRule: StrictnessRule.PeriodTimeBudget,
            floorRule: new DailyFloorRule.None(),
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(new DailyFloor(2))));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Break_KeepsTheBreakDayWhole()
    {
        var contract = Seal(workHours: NineToFive, sealedAt: SealedOnStart, allowCancellation: true);
        var breakDay = StartDate.AddDays(2);

        contract.Break(At.Utc(breakDay.ToDateTime(new TimeOnly(9, 5))), At.UtcZone);

        contract.EndsAt.Should().Be(breakDay.ToDateTime(new TimeOnly(17, 0)));
        contract.FloorHoursFor(breakDay).Should().Be(4);
    }

    [Fact]
    public void Seal_NoContractPresetIdGiven_DefaultsToNull()
    {
        var contract = Seal();

        contract.ContractPresetId.Should().BeNull();
    }

    [Fact]
    public void Seal_WithContractPresetId_StoresIt()
    {
        var presetId = Guid.NewGuid();

        var contract = Seal(contractPresetId: presetId);

        contract.ContractPresetId.Should().Be(presetId);
    }

    [Fact]
    public void IsInEffect_Scheduled_True()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        contract.IsInEffect.Should().BeTrue();
    }

    [Fact]
    public void IsInEffect_Active_True()
    {
        var contract = Seal(sealedAt: SealedOnStart); // Active

        contract.IsInEffect.Should().BeTrue();
    }

    [Fact]
    public void IsInEffect_Completed_False()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(At.Utc(SealedOnStart.AddDays(7)));

        contract.IsInEffect.Should().BeFalse();
    }

    [Fact]
    public void IsInEffect_Broken_False()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(At.Utc(SealedOnStart.AddDays(2)), At.UtcZone);

        contract.IsInEffect.Should().BeFalse();
    }

    [Fact]
    public void SetReflection_OnCompletedContract_TrimsAndStores()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(At.Utc(SealedOnStart.AddDays(7)));

        contract.SetReflection("  Went well  ");

        contract.Reflection.Should().Be("Went well");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetReflection_BlankValue_ClearsReflection(string? blank)
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(At.Utc(SealedOnStart.AddDays(7)));

        contract.SetReflection(blank);

        contract.Reflection.Should().BeNull();
    }

    [Fact]
    public void SetReflection_OnBrokenContract_Succeeds()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(At.Utc(SealedOnStart.AddDays(2)), At.UtcZone);

        contract.SetReflection("Had to stop early");

        contract.Reflection.Should().Be("Had to stop early");
    }

    [Fact]
    public void SetReflection_StillActive_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);

        var act = () => contract.SetReflection("Too soon");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkPostContractAcknowledged_OnCompletedContract_StampsTimestamp()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(At.Utc(SealedOnStart.AddDays(7)));
        var acknowledgedAt = SealedOnStart.AddDays(7);

        contract.MarkPostContractAcknowledged(At.Utc(acknowledgedAt));

        contract.PostContractAcknowledgedAt.Should().Be(At.Utc(acknowledgedAt));
    }

    [Fact]
    public void MarkPostContractAcknowledged_OnBrokenContract_Succeeds()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(At.Utc(SealedOnStart.AddDays(2)), At.UtcZone);
        var acknowledgedAt = SealedOnStart.AddDays(2);

        contract.MarkPostContractAcknowledged(At.Utc(acknowledgedAt));

        contract.PostContractAcknowledgedAt.Should().Be(At.Utc(acknowledgedAt));
    }

    [Fact]
    public void MarkPostContractAcknowledged_StillActive_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);

        var act = () => contract.MarkPostContractAcknowledged(At.Utc(SealedOnStart));

        act.Should().Throw<InvalidOperationException>();
    }

    // CheckRedemptionEligibility(proposedDate, today) is governed entirely by
    // RedemptionPlacementPolicy — never by StrictnessRule, and never by which day the
    // missed block was originally due (that date isn't even a parameter anymore; a
    // deficit stays claimable no matter how long it's been outstanding).

    [Fact]
    public void CheckRedemptionEligibility_AnywhereInContract_WithinRange_Allowed()
    {
        var contract = Seal(redemptionPlacementPolicy: RedemptionPlacementPolicy.AnywhereInContract);

        contract.CheckRedemptionEligibility(EndDate, StartDate).Should().Be(RedemptionEligibility.Allowed);
    }

    [Fact]
    public void CheckRedemptionEligibility_AnywhereInContract_OutsideRange_WrongDay()
    {
        var contract = Seal(redemptionPlacementPolicy: RedemptionPlacementPolicy.AnywhereInContract);

        contract.CheckRedemptionEligibility(EndDate.AddDays(1), StartDate).Should().Be(RedemptionEligibility.WrongDay);
        contract.CheckRedemptionEligibility(StartDate.AddDays(-1), StartDate).Should().Be(RedemptionEligibility.WrongDay);
    }

    [Fact]
    public void CheckRedemptionEligibility_CurrentDayOnly_MatchesToday_Allowed()
    {
        var contract = Seal(redemptionPlacementPolicy: RedemptionPlacementPolicy.CurrentDayOnly);
        var today = StartDate.AddDays(3);

        contract.CheckRedemptionEligibility(today, today).Should().Be(RedemptionEligibility.Allowed);
    }

    [Fact]
    public void CheckRedemptionEligibility_CurrentDayOnly_DoesNotMatchToday_WrongDay()
    {
        var contract = Seal(redemptionPlacementPolicy: RedemptionPlacementPolicy.CurrentDayOnly);
        var today = StartDate.AddDays(3);

        contract.CheckRedemptionEligibility(StartDate, today).Should().Be(RedemptionEligibility.WrongDay);
    }

    [Fact]
    public void CheckRedemptionEligibility_CurrentDayOnly_IgnoresWhenTheDeficitWasOriginallyDue()
    {
        // The bug this whole feature exists to fix: a deficit from days ago must still be
        // coverable today — eligibility only ever compares the *proposed* placement day
        // against today, never against the missed block's own original date.
        var contract = Seal(redemptionPlacementPolicy: RedemptionPlacementPolicy.CurrentDayOnly);
        var today = StartDate.AddDays(5);

        contract.CheckRedemptionEligibility(today, today).Should().Be(RedemptionEligibility.Allowed);
    }

    [Fact]
    public void AllowsBlockPlacement_NoWorkHours_AnyTimeOfDayAllowed()
    {
        var contract = Seal(workHours: null);

        contract.AllowsBlockPlacement(new TimeSlot(At.Utc(StartDate.ToDateTime(new TimeOnly(3, 0))), At.Utc(StartDate.ToDateTime(new TimeOnly(4, 0)))), new UserTimeZone(TimeZoneInfo.Utc))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_WithinWindow_Allowed()
    {
        var contract = Seal(workHours: NineToFive);

        contract.AllowsBlockPlacement(new TimeSlot(At.Utc(StartDate.ToDateTime(new TimeOnly(9, 0))), At.Utc(StartDate.ToDateTime(new TimeOnly(10, 0)))), new UserTimeZone(TimeZoneInfo.Utc))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_OutsideWindow_NotAllowed()
    {
        var contract = Seal(workHours: NineToFive);

        contract.AllowsBlockPlacement(new TimeSlot(At.Utc(StartDate.ToDateTime(new TimeOnly(20, 0))), At.Utc(StartDate.ToDateTime(new TimeOnly(21, 0)))), new UserTimeZone(TimeZoneInfo.Utc))
            .Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockPlacement_NoWorkHoursDay_IsAFullDay()
    {
        var contract = Seal(workHours: NineToFive, noWorkHoursDays: [StartDate]);

        contract.AllowsBlockPlacement(new TimeSlot(At.Utc(StartDate.ToDateTime(new TimeOnly(20, 0))), At.Utc(StartDate.ToDateTime(new TimeOnly(21, 0)))), new UserTimeZone(TimeZoneInfo.Utc))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_CrossMidnightBlock_BothDaysWithinTheirOwnWindow_Allowed()
    {
        // 23:00-01:00 — each day individually given a window allowing its part, rather than
        // the block being anchored to (and checked only against) its start day. Day 1's
        // window ends at 24:00 — the next day's midnight, exactly where the splitter ends
        // its segment.
        var day2 = StartDate.AddDays(1);
        var contract = Seal(floorRule: new DailyFloorRule.None(), calendar: DaysWithWindows(new()
        {
            [StartDate] = (StartDate.ToDateTime(new TimeOnly(22, 0)), day2.ToDateTime(TimeOnly.MinValue)),
            [day2] = (day2.ToDateTime(TimeOnly.MinValue), day2.ToDateTime(new TimeOnly(2, 0)))
        }));

        contract.AllowsBlockPlacement(new TimeSlot(At.Utc(StartDate.ToDateTime(new TimeOnly(23, 0))), At.Utc(StartDate.AddDays(1).ToDateTime(new TimeOnly(1, 0)))), new UserTimeZone(TimeZoneInfo.Utc))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_CrossMidnightBlock_OnlyTheSecondDayViolatesItsWindow_NotAllowed()
    {
        // Day 1 (up to midnight) is genuinely satisfied; Day 2's default 9-17 window
        // doesn't cover 00:00-01:00 — isolates that the rejection comes from Day 2's own
        // window, not (as the earlier, flawed version of this test accidentally allowed
        // to pass unnoticed) from Day 1 spuriously failing the midnight boundary too.
        var day2 = StartDate.AddDays(1);
        var contract = Seal(floorRule: new DailyFloorRule.None(), calendar: DaysWithWindows(new()
        {
            [StartDate] = (StartDate.ToDateTime(new TimeOnly(22, 0)), day2.ToDateTime(TimeOnly.MinValue)),
            [day2] = (day2.ToDateTime(new TimeOnly(9, 0)), day2.ToDateTime(new TimeOnly(17, 0))) // doesn't cover 00:00-01:00
        }));

        contract.AllowsBlockPlacement(new TimeSlot(At.Utc(StartDate.ToDateTime(new TimeOnly(23, 0))), At.Utc(StartDate.AddDays(1).ToDateTime(new TimeOnly(1, 0)))), new UserTimeZone(TimeZoneInfo.Utc))
            .Should().BeFalse();
    }

    [Fact]
    public void StartsAt_NoWorkHours_IsMidnightOnStartDate()
    {
        var contract = Seal(workHours: null);

        contract.StartsAt.Should().Be(StartDate.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void EndsAt_NoWorkHours_IsMidnightAfterEndDate()
    {
        var contract = Seal(workHours: null);

        contract.EndsAt.Should().Be(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void StartsAt_WithWorkHours_IsWorkHoursStart()
    {
        var contract = Seal(workHours: NineToFive);

        contract.StartsAt.Should().Be(StartDate.ToDateTime(new TimeOnly(9, 0)));
    }

    [Fact]
    public void EndsAt_WithWorkHours_IsWorkHoursEnd()
    {
        var contract = Seal(workHours: NineToFive);

        contract.EndsAt.Should().Be(EndDate.ToDateTime(new TimeOnly(17, 0)));
    }

    [Fact]
    public void StartsAt_NoWorkHoursOnStartDate_IsMidnight()
    {
        var contract = Seal(workHours: NineToFive, noWorkHoursDays: [StartDate]);

        contract.StartsAt.Should().Be(StartDate.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void EndsAt_NoWorkHoursOnEndDate_IsMidnightAfterIt()
    {
        var contract = Seal(workHours: NineToFive, noWorkHoursDays: [EndDate]);

        contract.EndsAt.Should().Be(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void IsRestDay_DayWithResolvedFloor_ReturnsFalse()
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)));

        contract.FloorHoursFor(StartDate).Should().NotBeNull();
    }

    [Fact]
    public void IsRestDay_DayWithNoResolvedFloor_ReturnsTrue()
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [StartDate]);

        contract.FloorHoursFor(StartDate).Should().BeNull();
    }

    [Fact]
    public void FloorHoursFor_DayWithResolvedFloor_ReturnsHours()
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)));

        contract.FloorHoursFor(StartDate).Should().Be(4);
    }

    [Fact]
    public void FloorHoursFor_RestDay_ReturnsNull()
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [StartDate]);

        contract.FloorHoursFor(StartDate).Should().BeNull();
    }

    // The whole-period target (Gap 95, decision 8)

    [Fact]
    public void HasPeriodTarget_OnlyWhenTheFloorRuleIsNone()
    {
        // The floor rule follows from the rules: None is Period Study with no fixed floor.
        Seal(strictnessRule: StrictnessRule.PeriodTimeBudget, floorRule: new DailyFloorRule.None()).HasPeriodTarget.Should().BeTrue();
        Seal(strictnessRule: StrictnessRule.PeriodTimeBudget, floorRule: new DailyFloorRule.Fixed(new DailyFloor(4))).HasPeriodTarget.Should().BeFalse();
        Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4))).HasPeriodTarget.Should().BeFalse();
        Seal(floorRule: new DailyFloorRule.FollowsPlan(), floorPlan: new Dictionary<DateOnly, double>()).HasPeriodTarget.Should().BeFalse();
    }

    [Fact]
    public void CommittedFocusHours_SumsPlannedFocusHoursByDay_AtSeal()
    {
        // Deliberately NOT the same as the daily floor total: a day can be planned above
        // its own floor (e.g. 4h scheduled against a 1h/day fixed floor) — the real
        // commitment is what was actually planned, not the minimum Seal's own validation
        // would have accepted.
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 4, [StartDate.AddDays(1)] = 1 };
        var contract = Seal(
            floorRule: new DailyFloorRule.FollowsPlan(),
            floorPlan: new Dictionary<DateOnly, double> { [StartDate] = 1, [StartDate.AddDays(1)] = 1 },
            plannedFocusHoursByDay: plan);

        contract.CommittedFocusHours.Should().Be(5);
    }

    [Fact]
    public void CommittedFocusHours_NoDailyFloorsAtAll_StillReflectsWhatWasPlanned()
    {
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 1 };
        var contract = Seal(floorRule: new DailyFloorRule.None(), plannedFocusHoursByDay: plan);

        contract.CommittedFocusHours.Should().Be(1);
    }

    [Theory]
    [InlineData(8.9, false)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    public void CheckFloor_WithAPeriodTarget_HoldsTheTotalToWhatWasSigned(double totalHours, bool allowed)
    {
        var contract = Seal(strictnessRule: StrictnessRule.PeriodTimeBudget, floorRule: new DailyFloorRule.None(),
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 9 });

        contract.CheckFloor(new Dictionary<DateOnly, double> { [StartDate.AddDays(1)] = totalHours })
            .Should().Be(allowed ? ChangeVerdict.Allowed : ChangeVerdict.BelowPeriodTarget(9));
    }

    [Fact]
    public void AllowsBlockPlacement_JudgesTheWindowOnTheUsersClock()
    {
        // Gap 88 phase 4: the time zone converts, not the caller. Window 09:00–17:00 local;
        // in Copenhagen winter 08:00 UTC is 09:00 on the user's clock.
        var copenhagen = new UserTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));
        var contract = Seal(floorRule: new DailyFloorRule.None(), calendar: DaysWithWindows(new()
        {
            [StartDate] = (StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(17, 0)))
        }));
        TimeSlot Utc(int fromHour, int toHour) =>
            new(At.Utc(StartDate.ToDateTime(new TimeOnly(fromHour, 0))), At.Utc(StartDate.ToDateTime(new TimeOnly(toHour, 0))));

        contract.AllowsBlockPlacement(Utc(8, 9), copenhagen).Should().BeTrue("09:00–10:00 on the user's clock");
        contract.AllowsBlockPlacement(Utc(7, 8), copenhagen).Should().BeFalse("08:00–09:00 on the user's clock");
    }

    // ── May this contract end? (Gap 95, decision 18) ───────────────────────────

    private static readonly Moment EndCheckAt = At.Utc(StartDate.ToDateTime(new TimeOnly(12, 0)));

    // Signed 2h on the first day; completed hours are the days' own (DayStatuses), so the tests
    // deliver them with real blocks.
    private static NenContract TwoHoursSigned() => Seal(floorRule: new DailyFloorRule.FollowsPlan(),
        floorPlan: new Dictionary<DateOnly, double> { [StartDate] = 2 }, plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 2 });

    private static FocusBlock WorkedBlock(NenContract contract, int fromHour, int toHour, double? endedAfterMinutes) =>
        TestBlocks.Worked(BlockAt(fromHour, toHour, TestContracts.Committed(contract.Id)), ritualAfterStart: 0, endedAfterStart: endedAfterMinutes);

    private static ContractEndVerdict Complete(NenContract contract, IEnumerable<PlannedBlock> blocks, bool sessionRunning = false) =>
        contract.CheckComplete(blocks, [], sessionRunning, EndCheckAt, At.UtcZone);

    [Fact]
    public void CheckComplete_EverythingDeliveredAndDone_IsAllowed()
    {
        var contract = TwoHoursSigned();

        var verdict = Complete(contract, [WorkedBlock(contract, 9, 11, endedAfterMinutes: 120)]);

        verdict.Outcome.Should().Be(ContractEndOutcome.Allowed);
        verdict.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CheckComplete_WhileASessionRuns_IsRefused_EvenWithEverythingDelivered()
    {
        var contract = TwoHoursSigned();

        Complete(contract, [WorkedBlock(contract, 9, 11, endedAfterMinutes: 120)], sessionRunning: true)
            .Outcome.Should().Be(ContractEndOutcome.SessionIsRunning);
    }

    [Fact]
    public void CheckComplete_HoursStillOwed_IsRefusedWithTheHoursLeft()
    {
        var contract = TwoHoursSigned();

        var verdict = Complete(contract, [WorkedBlock(contract, 9, 11, endedAfterMinutes: 30)]);

        verdict.Outcome.Should().Be(ContractEndOutcome.HoursNotDelivered);
        verdict.RemainingHours.Should().Be(1.5);
    }

    [Fact]
    public void CheckComplete_OverDelivered_HasNoHoursLeft()
    {
        var contract = TwoHoursSigned();

        Complete(contract, [WorkedBlock(contract, 8, 11, endedAfterMinutes: 180)]).RemainingHours.Should().Be(0);
    }

    [Fact]
    public void CheckComplete_ABlockNotYetFinal_IsRefusedWithTheCount_CancelledBlocksIgnored()
    {
        var contract = TwoHoursSigned();
        var delivered = WorkedBlock(contract, 8, 10, endedAfterMinutes: 120);
        var running = WorkedBlock(contract, 11, 13, endedAfterMinutes: null);
        var upcoming = BlockAt(14, 15, TestContracts.Committed(contract.Id));
        var cancelled = BlockAt(15, 16, TestContracts.Committed(contract.Id));
        cancelled.MarkCancelled(EndCheckAt);

        var verdict = Complete(contract, [delivered, running, upcoming, cancelled]);

        verdict.Outcome.Should().Be(ContractEndOutcome.BlocksStillPending);
        verdict.PendingBlockCount.Should().Be(2);
    }

    [Fact]
    public void CheckComplete_ABlockOfAnotherContract_Throws()
    {
        var contract = TwoHoursSigned();
        var foreign = BlockAt(14, 15, TestContracts.Committed(Guid.NewGuid()));

        var act = () => Complete(contract, [foreign]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(true, ContractEndOutcome.SessionIsRunning)]
    [InlineData(false, ContractEndOutcome.Allowed)]
    public void CheckBreak_IsRefusedOnlyWhileASessionRuns(bool sessionRunning, ContractEndOutcome expected)
    {
        Seal(allowCancellation: true).CheckBreak(sessionRunning).Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // the setting never changes, so it is the reason given even while a session runs
    public void CheckBreak_CancellationOff_IsRefused(bool sessionRunning)
    {
        // The contract's own setting is the gate (decided 2026-10-06, ending the 2026-08-18 interim).
        Seal(allowCancellation: false).CheckBreak(sessionRunning).Outcome.Should().Be(ContractEndOutcome.CancellationNotAllowed);
    }
}
