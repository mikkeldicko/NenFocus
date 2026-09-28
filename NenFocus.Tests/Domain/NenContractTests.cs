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
        ContractFloor? contractFloor = null,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract)
    {
        var rule = floorRule ?? (contractFloor is not null ? new DailyFloorRule.None() : new DailyFloorRule.Fixed(new DailyFloor(4)));
        var plan = plannedFocusHoursByDay ?? FullyPlanned();
        return NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            strictnessRule: strictnessRule,
            calendar: calendar ?? TestCalendars.For(StartDate, EndDate, rule, floorPlan ?? plan, workHours, restDays, noWorkHoursDays),
            floorRule: rule,
            changeWindow: changeWindow,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plan,
            localSealedAt: sealedAt ?? SealedOnStart,
            contractPresetId: contractPresetId,
            contractFloor: contractFloor,
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
        var act = () => NenContract.Seal(
            id: Guid.NewGuid(),
            name: "   ",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: FullyPlanned(),
            localSealedAt: SealedOnStart);

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

        contract.Activate(SealedOnStart);

        contract.Status.Should().Be(NenContractStatus.Active);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractActivatedEvent);
    }

    [Fact]
    public void Activate_WhenNotScheduled_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart); // already Active

        var act = () => contract.Activate(SealedOnStart);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_WhenActive_TransitionsToCompletedAndRaisesEvent()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.ClearDomainEvents();

        contract.Complete(SealedOnStart.AddDays(7));

        contract.Status.Should().Be(NenContractStatus.Completed);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractCompletedEvent);
    }

    [Fact]
    public void Complete_WhenScheduled_Throws()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        var act = () => contract.Complete(SealedOnStart);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(SealedOnStart.AddDays(7));

        var act = () => contract.Complete(SealedOnStart.AddDays(8));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Break_WhenActiveAndCancellationAllowed_TransitionsToBrokenAndRaisesEvent()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.ClearDomainEvents();

        contract.Break(SealedOnStart.AddDays(2));

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

        contract.Break(SealedOnStart.AddDays(2));

        contract.Status.Should().Be(NenContractStatus.Broken);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractBrokenEvent);
    }

    [Fact]
    public void Break_WhenScheduled_TransitionsToBrokenAndRaisesEvent()
    {
        // A not-yet-started contract is still "in effect" and can be cancelled before Day 1.
        var contract = Seal(sealedAt: SealedBeforeStart, allowCancellation: true); // Scheduled
        contract.ClearDomainEvents();

        contract.Break(SealedOnStart);

        contract.Status.Should().Be(NenContractStatus.Broken);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractBrokenEvent);
    }

    [Fact]
    public void Break_WhenAlreadyCompleted_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true); // Active
        contract.Complete(SealedOnStart.AddDays(8));

        var act = () => contract.Break(SealedOnStart.AddDays(9));

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

        contract.Break(breakDate.ToDateTime(new TimeOnly(14, 0)));

        contract.EndDate.Should().Be(breakDate);
    }

    [Fact]
    public void Break_WhileActive_PreservesOriginalEndDate()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);

        contract.Break(StartDate.AddDays(2).ToDateTime(new TimeOnly(14, 0)));

        contract.OriginalEndDate.Should().Be(EndDate);
    }

    [Fact]
    public void Break_BeforeContractEverStarted_ClampsEndDateToStartDate()
    {
        // A Scheduled contract broken before Day 1 ever arrives — EndDate must not end up
        // before StartDate (would violate Seal's own invariant), so it clamps to StartDate
        // rather than to the literal break date.
        var contract = Seal(sealedAt: SealedBeforeStart, allowCancellation: true); // Scheduled

        contract.Break(SealedBeforeStart);

        contract.EndDate.Should().Be(StartDate);
        contract.OriginalEndDate.Should().Be(EndDate); // OriginalEndDate is never touched by Break
    }

    [Theory]
    [InlineData(3.9, false)] // below floor -> not met
    [InlineData(4, true)]    // exactly at floor -> met
    [InlineData(5, true)]    // above floor -> met
    public void IsDayMet_ComparesHoursAgainstFloor(double completedHours, bool expectedMet)
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)));

        contract.IsDayMet(StartDate, completedHours).Should().Be(expectedMet);
    }

    [Fact]
    public void IsDayMet_MetEvenWhenABlockWasMissed_AsLongAsHoursMeetFloor()
    {
        // The day verdict is hours-only now — a missed/late block only costs hours
        // that must be made up elsewhere; it does not fail the day directly.
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)));

        contract.IsDayMet(StartDate, completedFocusHours: 4).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IsDayMet_FloorlessContract_AlwaysMet(double completedHours)
    {
        var contract = Seal(floorRule: new DailyFloorRule.None(), plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

        contract.IsDayMet(StartDate, completedHours).Should().BeTrue();
    }

    [Fact]
    public void IsDayMet_RestDayViaPerDayException_AlwaysMet()
    {
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(5));
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [StartDate.AddDays(5)], plannedFocusHoursByDay: plan);

        contract.IsDayMet(StartDate.AddDays(5), completedFocusHours: 0).Should().BeTrue();
    }

    [Fact]
    public void IsDayMet_DifferentDaysHaveDifferentFloors()
    {
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 2, [StartDate.AddDays(1)] = 3 };
        var contract = Seal(floorRule: new DailyFloorRule.FollowsPlan(), plannedFocusHoursByDay: plan);

        contract.IsDayMet(StartDate, completedFocusHours: 2).Should().BeTrue();
        contract.IsDayMet(StartDate.AddDays(1), completedFocusHours: 2).Should().BeFalse(); // below its own 3h floor
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(2, 0.5)]
    [InlineData(4, 1.0)]
    [InlineData(5.32, 1.33)] // uncapped
    public void PercentOfFloorMet_DelegatesToDailyFloor(double completedHours, double expected)
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)));

        contract.PercentOfFloorMet(StartDate, completedHours).Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void PercentOfFloorMet_RestDay_ReturnsNull()
    {
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(5));
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [StartDate.AddDays(5)], plannedFocusHoursByDay: plan);

        contract.PercentOfFloorMet(StartDate.AddDays(5), completedFocusHours: 2).Should().BeNull();
    }

    [Fact]
    public void PercentOfFloorMet_FloorlessContract_ReturnsNull()
    {
        var contract = Seal(floorRule: new DailyFloorRule.None(), plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

        contract.PercentOfFloorMet(StartDate, completedFocusHours: 2).Should().BeNull();
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
    public void Covers_IsTheHalfOpenPeriodFromStartsAtToEndsAt()
    {
        var contract = Seal(workHours: NineToFive);

        contract.Covers(StartDate.ToDateTime(new TimeOnly(8, 59))).Should().BeFalse();
        contract.Covers(StartDate.ToDateTime(new TimeOnly(9, 0))).Should().BeTrue();
        contract.Covers(EndDate.ToDateTime(new TimeOnly(16, 59))).Should().BeTrue();
        contract.Covers(EndDate.ToDateTime(new TimeOnly(17, 0))).Should().BeFalse();
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
        var act = () => Seal(
            floorRule: new DailyFloorRule.None(),
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(new DailyFloor(2))));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Break_KeepsTheBreakDayWhole()
    {
        var contract = Seal(workHours: NineToFive, sealedAt: SealedOnStart, allowCancellation: true);
        var breakDay = StartDate.AddDays(2);

        contract.Break(breakDay.ToDateTime(new TimeOnly(9, 5)));

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
        contract.Complete(SealedOnStart.AddDays(7));

        contract.IsInEffect.Should().BeFalse();
    }

    [Fact]
    public void IsInEffect_Broken_False()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(SealedOnStart.AddDays(2));

        contract.IsInEffect.Should().BeFalse();
    }

    [Fact]
    public void SetReflection_OnCompletedContract_TrimsAndStores()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(SealedOnStart.AddDays(7));

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
        contract.Complete(SealedOnStart.AddDays(7));

        contract.SetReflection(blank);

        contract.Reflection.Should().BeNull();
    }

    [Fact]
    public void SetReflection_OnBrokenContract_Succeeds()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(SealedOnStart.AddDays(2));

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
        contract.Complete(SealedOnStart.AddDays(7));
        var acknowledgedAt = SealedOnStart.AddDays(7);

        contract.MarkPostContractAcknowledged(acknowledgedAt);

        contract.PostContractAcknowledgedAt.Should().Be(acknowledgedAt);
    }

    [Fact]
    public void MarkPostContractAcknowledged_OnBrokenContract_Succeeds()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(SealedOnStart.AddDays(2));
        var acknowledgedAt = SealedOnStart.AddDays(2);

        contract.MarkPostContractAcknowledged(acknowledgedAt);

        contract.PostContractAcknowledgedAt.Should().Be(acknowledgedAt);
    }

    [Fact]
    public void MarkPostContractAcknowledged_StillActive_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);

        var act = () => contract.MarkPostContractAcknowledged(SealedOnStart);

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

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(3, 0)), StartDate.ToDateTime(new TimeOnly(4, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_WithinWindow_Allowed()
    {
        var contract = Seal(workHours: NineToFive);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_OutsideWindow_NotAllowed()
    {
        var contract = Seal(workHours: NineToFive);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(20, 0)), StartDate.ToDateTime(new TimeOnly(21, 0)))
            .Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockPlacement_NoWorkHoursDay_IsAFullDay()
    {
        var contract = Seal(workHours: NineToFive, noWorkHoursDays: [StartDate]);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(20, 0)), StartDate.ToDateTime(new TimeOnly(21, 0)))
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

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(23, 0)), StartDate.AddDays(1).ToDateTime(new TimeOnly(1, 0)))
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

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(23, 0)), StartDate.AddDays(1).ToDateTime(new TimeOnly(1, 0)))
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

        contract.IsRestDay(StartDate).Should().BeFalse();
    }

    [Fact]
    public void IsRestDay_DayWithNoResolvedFloor_ReturnsTrue()
    {
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [StartDate]);

        contract.IsRestDay(StartDate).Should().BeTrue();
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

    [Fact]
    public void AllowsBlockTimeChange_Locked_NeverAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.Locked);

        contract.AllowsBlockTimeChange(StartDate, StartDate, StartDate).Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockTimeChange_DailyFlexibility_SameDayAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.DailyFlexibility);

        contract.AllowsBlockTimeChange(StartDate, StartDate, StartDate).Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockTimeChange_DailyFlexibility_DifferentStartDayNotAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.DailyFlexibility);

        contract.AllowsBlockTimeChange(StartDate, StartDate.AddDays(1), StartDate.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockTimeChange_DailyFlexibility_SpanningIntoNextDayNotAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.DailyFlexibility);

        // Starts on the original day but the (resized) end crosses into the next day.
        contract.AllowsBlockTimeChange(StartDate, StartDate, StartDate.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockTimeChange_PeriodTimeBudget_WithinContractRangeAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.PeriodTimeBudget);

        contract.AllowsBlockTimeChange(StartDate, StartDate.AddDays(2), StartDate.AddDays(3)).Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockTimeChange_PeriodTimeBudget_BeforeContractStartNotAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.PeriodTimeBudget);

        contract.AllowsBlockTimeChange(StartDate, StartDate.AddDays(-1), StartDate).Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockTimeChange_PeriodTimeBudget_AfterContractEndNotAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.PeriodTimeBudget);

        contract.AllowsBlockTimeChange(StartDate, EndDate, EndDate.AddDays(1)).Should().BeFalse();
    }

    // ── ContractFloor ────────────────────────────────────────────────────────────

    [Fact]
    public void Seal_ContractFloorWithAFixedDailyFloor_Throws()
    {
        var act = () => Seal(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)),
            contractFloor: new ContractFloor(9));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_ContractFloorWithADayCarryingAFloor_Throws()
    {
        var act = () => Seal(
            floorRule: new DailyFloorRule.FollowsPlan(),
            floorPlan: new Dictionary<DateOnly, double> { [StartDate] = 2 },
            contractFloor: new ContractFloor(9));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_ContractFloorWithNoDailyFloors_Succeeds()
    {
        var act = () => Seal(floorRule: new DailyFloorRule.None(), contractFloor: new ContractFloor(9));

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_ContractFloorWithOnlyRestDays_Succeeds()
    {
        // Days with no floor carry no real obligation — only a day with a floor should
        // conflict with ContractFloor.
        var act = () => Seal(
            floorRule: new DailyFloorRule.FollowsPlan(),
            floorPlan: new Dictionary<DateOnly, double>(),
            contractFloor: new ContractFloor(9));

        act.Should().NotThrow();
    }

    [Fact]
    public void ContractFloorHours_WhenSet_ReturnsHours()
    {
        var contract = Seal(contractFloor: new ContractFloor(9));

        contract.ContractFloorHours.Should().Be(9);
    }

    [Fact]
    public void ContractFloorHours_WhenNotSet_ReturnsNull()
    {
        var contract = Seal();

        contract.ContractFloorHours.Should().BeNull();
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

    [Fact]
    public void CommittedFocusHours_MatchesContractFloorHours_ForAContractFloorModeContract()
    {
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 9 };
        var contract = Seal(floorRule: new DailyFloorRule.None(), plannedFocusHoursByDay: plan, contractFloor: new ContractFloor(9));

        contract.CommittedFocusHours.Should().Be(contract.ContractFloorHours);
    }

    [Theory]
    [InlineData(8.9, false)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    public void IsPeriodMet_ComparesTotalAgainstContractFloor(double totalHours, bool expectedMet)
    {
        var contract = Seal(contractFloor: new ContractFloor(9));

        contract.IsPeriodMet(totalHours).Should().Be(expectedMet);
    }

    [Fact]
    public void IsPeriodMet_NoContractFloor_AlwaysMet()
    {
        var contract = Seal();

        contract.IsPeriodMet(0).Should().BeTrue();
    }
}
