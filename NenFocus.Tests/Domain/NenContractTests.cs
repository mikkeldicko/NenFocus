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
        Guid? contractPresetId = null,
        ContractFloor? contractFloor = null,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract)
        => NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            startDate: StartDate,
            endDate: EndDate,
            strictnessRule: strictnessRule,
            dailyFloorPolicy: dailyFloorPolicy ?? (contractFloor is not null ? DailyFloorPolicy.None : new DailyFloorPolicy(new DailyFloor(4))),
            changeWindow: changeWindow,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plannedFocusHoursByDay ?? FullyPlanned(),
            sealedAt: sealedAt ?? SealedOnStart,
            workHoursPolicy: workHoursPolicy,
            contractPresetId: contractPresetId,
            contractFloor: contractFloor,
            redemptionPlacementPolicy: redemptionPlacementPolicy);

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
    public void AllowsBlockPlacement_NoWorkHoursPolicy_AlwaysAllowed()
    {
        var contract = Seal(workHoursPolicy: null);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(3, 0)), StartDate.ToDateTime(new TimeOnly(4, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_WithinWindow_Allowed()
    {
        var policy = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_OutsideWindow_NotAllowed()
    {
        var policy = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(20, 0)), StartDate.ToDateTime(new TimeOnly(21, 0)))
            .Should().BeFalse();
    }

    [Fact]
    public void AllowsBlockPlacement_PerDayExceptionOverridesDefault()
    {
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            new Dictionary<DateOnly, WorkHours?> { [StartDate] = null }); // no constraint that day
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(20, 0)), StartDate.ToDateTime(new TimeOnly(21, 0)))
            .Should().BeTrue();
    }

    [Fact]
    public void AllowsBlockPlacement_CrossMidnightBlock_BothDaysWithinTheirOwnWindow_Allowed()
    {
        // 23:00-01:00 — each day individually configured to allow that late window,
        // rather than the block being anchored to (and checked only against) its start day.
        // Day 1's window ends at TimeOnly.MaxValue — as close to midnight as representable,
        // since TimeOnly can't express "24:00" itself; the day-1 segment produced by the
        // splitter ends exactly at midnight, resolved via the same one-tick-back idiom
        // AllowsBlockPlacement applies internally.
        var lateWindow = new WorkHours(new TimeOnly(22, 0), TimeOnly.MaxValue);
        var earlyWindow = new WorkHours(new TimeOnly(0, 0), new TimeOnly(2, 0));
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            new Dictionary<DateOnly, WorkHours?> { [StartDate] = lateWindow, [StartDate.AddDays(1)] = earlyWindow });
        var contract = Seal(workHoursPolicy: policy);

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
        var lateWindow = new WorkHours(new TimeOnly(22, 0), TimeOnly.MaxValue);
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)), // default window doesn't cover 00:00-01:00 either
            new Dictionary<DateOnly, WorkHours?> { [StartDate] = lateWindow });
        var contract = Seal(workHoursPolicy: policy);

        contract.AllowsBlockPlacement(StartDate.ToDateTime(new TimeOnly(23, 0)), StartDate.AddDays(1).ToDateTime(new TimeOnly(1, 0)))
            .Should().BeFalse();
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
    public void FloorHoursFor_DayWithResolvedFloor_ReturnsHours()
    {
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)));

        contract.FloorHoursFor(StartDate).Should().Be(4);
    }

    [Fact]
    public void FloorHoursFor_RestDay_ReturnsNull()
    {
        var policy = new DailyFloorPolicy(new DailyFloor(4), new Dictionary<DateOnly, DailyFloor?> { [StartDate] = null });
        var contract = Seal(dailyFloorPolicy: policy);

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
    public void Seal_ContractFloorWithRealDailyFloorPolicyDefault_Throws()
    {
        var act = () => Seal(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)),
            contractFloor: new ContractFloor(9));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_ContractFloorWithRealPerDayException_Throws()
    {
        var policy = new DailyFloorPolicy(null, new Dictionary<DateOnly, DailyFloor?> { [StartDate] = new DailyFloor(2) });

        var act = () => Seal(dailyFloorPolicy: policy, contractFloor: new ContractFloor(9));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_ContractFloorWithDailyFloorPolicyNone_Succeeds()
    {
        var act = () => Seal(dailyFloorPolicy: DailyFloorPolicy.None, contractFloor: new ContractFloor(9));

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_ContractFloorWithRestDayOnlyExceptions_Succeeds()
    {
        // Per-day exceptions that are all explicit rest days (null) carry no real
        // obligation — only a non-null exception should conflict with ContractFloor.
        var policy = new DailyFloorPolicy(null, new Dictionary<DateOnly, DailyFloor?> { [StartDate] = null });

        var act = () => Seal(dailyFloorPolicy: policy, contractFloor: new ContractFloor(9));

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
        var policy = new DailyFloorPolicy(null, new Dictionary<DateOnly, DailyFloor?>
        {
            [StartDate] = new DailyFloor(1),
            [StartDate.AddDays(1)] = new DailyFloor(1)
        });
        var contract = Seal(dailyFloorPolicy: policy, plannedFocusHoursByDay: plan);

        contract.CommittedFocusHours.Should().Be(5);
    }

    [Fact]
    public void CommittedFocusHours_NoDailyFloorPolicyAtAll_StillReflectsWhatWasPlanned()
    {
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 1 };
        var contract = Seal(dailyFloorPolicy: DailyFloorPolicy.None, plannedFocusHoursByDay: plan);

        contract.CommittedFocusHours.Should().Be(1);
    }

    [Fact]
    public void CommittedFocusHours_MatchesContractFloorHours_ForAContractFloorModeContract()
    {
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 9 };
        var contract = Seal(dailyFloorPolicy: DailyFloorPolicy.None, plannedFocusHoursByDay: plan, contractFloor: new ContractFloor(9));

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
