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

    private static NenContract Seal(
        DailyFloorPolicy? dailyFloorPolicy = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        DateTime? sealedAt = null,
        bool allowCancellation = false,
        ChangeWindow? changeWindow = null,
        StrictnessRule strictnessRule = StrictnessRule.Locked,
        WorkHoursPolicy? workHoursPolicy = null,
        Guid? contractPresetId = null)
        => NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            startDate: StartDate,
            endDate: EndDate,
            strictnessRule: strictnessRule,
            dailyFloorPolicy: dailyFloorPolicy ?? new DailyFloorPolicy(new DailyFloor(4)),
            changeWindow: changeWindow,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plannedFocusHoursByDay ?? FullyPlanned(),
            sealedAt: sealedAt ?? SealedOnStart,
            workHoursPolicy: workHoursPolicy,
            contractPresetId: contractPresetId);

    [Fact]
    public void Seal_EndBeforeStart_Throws()
    {
        var act = () => NenContract.Seal(
            Guid.NewGuid(), "Name", new NenContractGoal("Goal"),
            startDate: EndDate, endDate: StartDate,
            StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), SealedOnStart);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_BlankName_Throws()
    {
        var act = () => NenContract.Seal(
            Guid.NewGuid(), "   ", new NenContractGoal("Goal"),
            StartDate, EndDate,
            StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
            FullyPlanned(), SealedOnStart);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_EveryDayMeetsFloor_Succeeds()
    {
        var act = () => Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), plannedFocusHoursByDay: FullyPlanned(4));

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_OneDayBelowFloor_Throws()
    {
        var plan = FullyPlanned(4);
        plan[StartDate.AddDays(3)] = 2; // below the floor of 4

        var act = () => Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), plannedFocusHoursByDay: plan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_DayMissingFromMap_TreatedAsZeroAndThrows()
    {
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(2));

        var act = () => Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), plannedFocusHoursByDay: plan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_FloorDisabled_NoPlannedHoursRequired()
    {
        var act = () => Seal(dailyFloorPolicy: DailyFloorPolicy.None, plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_RestDayViaPerDayException_SkippedEvenWithNoPlannedHours()
    {
        // Default floor of 4h applies every day except the explicit rest-day exception.
        var policy = new DailyFloorPolicy(new DailyFloor(4), new Dictionary<DateOnly, DailyFloor?>
        {
            [StartDate.AddDays(5)] = null
        });
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(5)); // no hours planned on the rest day

        var act = () => Seal(dailyFloorPolicy: policy, plannedFocusHoursByDay: plan);

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
        var perDayFloors = plan.ToDictionary(kv => kv.Key, kv => (DailyFloor?)new DailyFloor(kv.Value));
        var policy = new DailyFloorPolicy(null, perDayFloors);

        var act = () => Seal(dailyFloorPolicy: policy, plannedFocusHoursByDay: plan);

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
    public void Break_WhenCancellationNotAllowed_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: false);

        var act = () => contract.Break(SealedOnStart.AddDays(2));

        act.Should().Throw<InvalidOperationException>();
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

    [Theory]
    [InlineData(3.9, false)] // below floor -> not met
    [InlineData(4, true)]    // exactly at floor -> met
    [InlineData(5, true)]    // above floor -> met
    public void IsDayMet_ComparesHoursAgainstFloor(double completedHours, bool expectedMet)
    {
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)));

        contract.IsDayMet(StartDate, completedHours).Should().Be(expectedMet);
    }

    [Fact]
    public void IsDayMet_MetEvenWhenABlockWasMissed_AsLongAsHoursMeetFloor()
    {
        // The day verdict is hours-only now — a missed/late block only costs hours
        // that must be made up elsewhere; it does not fail the day directly.
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)));

        contract.IsDayMet(StartDate, completedFocusHours: 4).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IsDayMet_FloorlessContract_AlwaysMet(double completedHours)
    {
        var contract = Seal(dailyFloorPolicy: DailyFloorPolicy.None, plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

        contract.IsDayMet(StartDate, completedHours).Should().BeTrue();
    }

    [Fact]
    public void IsDayMet_RestDayViaPerDayException_AlwaysMet()
    {
        var policy = new DailyFloorPolicy(new DailyFloor(4), new Dictionary<DateOnly, DailyFloor?>
        {
            [StartDate.AddDays(5)] = null
        });
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(5));
        var contract = Seal(dailyFloorPolicy: policy, plannedFocusHoursByDay: plan);

        contract.IsDayMet(StartDate.AddDays(5), completedFocusHours: 0).Should().BeTrue();
    }

    [Fact]
    public void IsDayMet_DifferentDaysHaveDifferentFloors()
    {
        var policy = new DailyFloorPolicy(null, new Dictionary<DateOnly, DailyFloor?>
        {
            [StartDate] = new DailyFloor(2),
            [StartDate.AddDays(1)] = new DailyFloor(3)
        });
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 2, [StartDate.AddDays(1)] = 3 };
        var contract = Seal(dailyFloorPolicy: policy, plannedFocusHoursByDay: plan);

        contract.IsDayMet(StartDate, completedFocusHours: 2).Should().BeTrue();
        contract.IsDayMet(StartDate.AddDays(1), completedFocusHours: 2).Should().BeFalse(); // below its own 3h floor
    }

    [Fact]
    public void Seal_NoWorkHoursPolicyGiven_DefaultsToNull()
    {
        var contract = Seal();

        contract.WorkHoursPolicy.Should().BeNull();
    }

    [Fact]
    public void Seal_WithWorkHoursPolicy_StoresIt()
    {
        var policy = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));

        var contract = Seal(workHoursPolicy: policy);

        contract.WorkHoursPolicy.Should().Be(policy);
    }

    private static Dictionary<DateOnly, double> PlannedForNewDays(DateOnly from, DateOnly to, double hoursPerDay = 4)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var date = from; date <= to; date = date.AddDays(1))
            map[date] = hoursPerDay;
        return map;
    }

    [Fact]
    public void Extend_WhenActive_PushesEndDateForwardAndRaisesEvent()
    {
        var contract = Seal(sealedAt: SealedOnStart); // Active
        contract.ClearDomainEvents();
        var newEndDate = EndDate.AddDays(3);

        contract.Extend(newEndDate, PlannedForNewDays(EndDate.AddDays(1), newEndDate), SealedOnStart.AddDays(2));

        contract.EndDate.Should().Be(newEndDate);
        contract.Status.Should().Be(NenContractStatus.Active);
        contract.DomainEvents.Should().ContainSingle(e => e is NenContractExtendedEvent);
    }

    [Fact]
    public void Extend_RaisesEventWithOldAndNewEndDates()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.ClearDomainEvents();
        var newEndDate = EndDate.AddDays(3);

        contract.Extend(newEndDate, PlannedForNewDays(EndDate.AddDays(1), newEndDate), SealedOnStart.AddDays(2));

        var raised = contract.DomainEvents.OfType<NenContractExtendedEvent>().Single();
        raised.ContractId.Should().Be(contract.Id);
        raised.OldEndDate.Should().Be(EndDate);
        raised.NewEndDate.Should().Be(newEndDate);
    }

    [Fact]
    public void Extend_WhenScheduled_Throws()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled
        var newEndDate = EndDate.AddDays(3);

        var act = () => contract.Extend(newEndDate, PlannedForNewDays(EndDate.AddDays(1), newEndDate), SealedOnStart);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Extend_WhenCompleted_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(SealedOnStart.AddDays(7));
        var newEndDate = EndDate.AddDays(3);

        var act = () => contract.Extend(newEndDate, PlannedForNewDays(EndDate.AddDays(1), newEndDate), SealedOnStart.AddDays(8));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Extend_NewEndDateNotAfterCurrentEndDate_Throws()
    {
        var contract = Seal(sealedAt: SealedOnStart);

        var act = () => contract.Extend(EndDate, new Dictionary<DateOnly, double>(), SealedOnStart.AddDays(2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extend_NewDayBelowFloor_Throws()
    {
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var plan = PlannedForNewDays(EndDate.AddDays(1), newEndDate, 4);
        plan[newEndDate] = 1; // below the floor of 4

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extend_NewDayMissingFromMap_TreatedAsZeroAndThrows()
    {
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var plan = PlannedForNewDays(EndDate.AddDays(1), newEndDate.AddDays(-1), 4); // missing newEndDate itself

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extend_DoesNotReCheckExistingDays()
    {
        // Existing days were already validated at Seal; Extend only floor-checks the new days.
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var plan = PlannedForNewDays(EndDate.AddDays(1), newEndDate, 4);

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2));

        act.Should().NotThrow();
    }

    [Fact]
    public void Extend_WithPerDayFloorOverrides_MergesIntoPolicyAndAppliesGoingForward()
    {
        // Simple-contract-style: no Default, so the extended days need explicit floors
        // or they'd resolve to rest days.
        var contract = Seal(dailyFloorPolicy: DailyFloorPolicy.None, sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var day1 = EndDate.AddDays(1);
        var day2 = EndDate.AddDays(2);
        var plan = new Dictionary<DateOnly, double> { [day1] = 2, [day2] = 3 };
        var overrides = new Dictionary<DateOnly, DailyFloor?>
        {
            [day1] = new DailyFloor(2),
            [day2] = new DailyFloor(3)
        };

        contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2), overrides);

        contract.IsDayMet(day1, completedFocusHours: 2).Should().BeTrue();
        contract.IsDayMet(day2, completedFocusHours: 2).Should().BeFalse(); // below its own 3h floor
    }

    [Fact]
    public void Extend_OverrideBelowFloor_Throws()
    {
        var contract = Seal(dailyFloorPolicy: DailyFloorPolicy.None, sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(1);
        var day1 = EndDate.AddDays(1);
        var plan = new Dictionary<DateOnly, double> { [day1] = 1 };
        var overrides = new Dictionary<DateOnly, DailyFloor?> { [day1] = new DailyFloor(2) };

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2), overrides);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsDueForActivation_ScheduledAndStartDateArrived_True()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        contract.IsDueForActivation(StartDate).Should().BeTrue();
    }

    [Fact]
    public void IsDueForActivation_ScheduledButStartDateNotYetArrived_False()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        contract.IsDueForActivation(StartDate.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsDueForActivation_AlreadyActive_False()
    {
        var contract = Seal(sealedAt: SealedOnStart); // Active

        contract.IsDueForActivation(StartDate).Should().BeFalse();
    }

    [Fact]
    public void IsDueForCompletion_ActiveAndEndDatePassed_True()
    {
        var contract = Seal(sealedAt: SealedOnStart); // Active

        contract.IsDueForCompletion(EndDate.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void IsDueForCompletion_OnTheEndDateItself_False()
    {
        // EndDate is inclusive — the contract is still running through its last day.
        var contract = Seal(sealedAt: SealedOnStart); // Active

        contract.IsDueForCompletion(EndDate).Should().BeFalse();
    }

    [Fact]
    public void IsDueForCompletion_StillScheduled_False()
    {
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled

        contract.IsDueForCompletion(EndDate.AddDays(1)).Should().BeFalse();
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
    public void CheckMakeUpEligibility_Locked_AlwaysNotAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.Locked);

        contract.CheckMakeUpEligibility(StartDate, StartDate).Should().Be(MakeUpEligibility.NotAllowed);
        contract.CheckMakeUpEligibility(StartDate, StartDate.AddDays(1)).Should().Be(MakeUpEligibility.NotAllowed);
    }

    [Fact]
    public void CheckMakeUpEligibility_DailyFlexibility_SameDayAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.DailyFlexibility);

        contract.CheckMakeUpEligibility(StartDate, StartDate).Should().Be(MakeUpEligibility.Allowed);
    }

    [Fact]
    public void CheckMakeUpEligibility_DailyFlexibility_DifferentDayIsWrongDay()
    {
        var contract = Seal(strictnessRule: StrictnessRule.DailyFlexibility);

        contract.CheckMakeUpEligibility(StartDate, StartDate.AddDays(1)).Should().Be(MakeUpEligibility.WrongDay);
    }

    [Fact]
    public void CheckMakeUpEligibility_PeriodTimeBudget_AnyDayAllowed()
    {
        var contract = Seal(strictnessRule: StrictnessRule.PeriodTimeBudget);

        contract.CheckMakeUpEligibility(StartDate, StartDate.AddDays(3)).Should().Be(MakeUpEligibility.Allowed);
    }

    [Fact]
    public void AllowsBlockPlacement_NoWorkHoursPolicy_AlwaysAllowed()
    {
        var contract = Seal(workHoursPolicy: null);

        contract.AllowsBlockPlacement(StartDate, StartDate.ToDateTime(new TimeOnly(3, 0)), StartDate.ToDateTime(new TimeOnly(4, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_WithinWindow_Allowed()
    {
        var policy = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate, StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_OutsideWindow_NotAllowed()
    {
        var policy = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate, StartDate.ToDateTime(new TimeOnly(20, 0)), StartDate.ToDateTime(new TimeOnly(21, 0)))
            .Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockPlacement_PerDayExceptionOverridesDefault()
    {
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            new Dictionary<DateOnly, WorkHours?> { [StartDate] = null }); // no constraint that day
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate, StartDate.ToDateTime(new TimeOnly(20, 0)), StartDate.ToDateTime(new TimeOnly(21, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void IsRestDay_DayWithResolvedFloor_ReturnsFalse()
    {
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)));

        contract.IsRestDay(StartDate).Should().BeFalse();
    }

    [Fact]
    public void IsRestDay_DayWithNoResolvedFloor_ReturnsTrue()
    {
        var policy = new DailyFloorPolicy(new DailyFloor(4), new Dictionary<DateOnly, DailyFloor?> { [StartDate] = null });
        var contract = Seal(dailyFloorPolicy: policy);

        contract.IsRestDay(StartDate).Should().BeTrue();
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
}
