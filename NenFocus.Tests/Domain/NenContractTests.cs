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
        DailyFloor? dailyFloor = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        DateTime? sealedAt = null,
        bool allowCancellation = false,
        ChangeWindow? changeWindow = null,
        StrictnessRule strictnessRule = StrictnessRule.Locked,
        WorkHoursPolicy? workHoursPolicy = null)
        => NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            startDate: StartDate,
            endDate: EndDate,
            strictnessRule: strictnessRule,
            dailyFloor: dailyFloor ?? new DailyFloor(4),
            changeWindow: changeWindow,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plannedFocusHoursByDay ?? FullyPlanned(),
            sealedAt: sealedAt ?? SealedOnStart,
            workHoursPolicy: workHoursPolicy);

    [Fact]
    public void Seal_EndBeforeStart_Throws()
    {
        var act = () => NenContract.Seal(
            Guid.NewGuid(), "Name", new NenContractGoal("Goal"),
            startDate: EndDate, endDate: StartDate,
            StrictnessRule.Locked, new DailyFloor(0), null, false,
            new Dictionary<DateOnly, double>(), SealedOnStart);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_BlankName_Throws()
    {
        var act = () => NenContract.Seal(
            Guid.NewGuid(), "   ", new NenContractGoal("Goal"),
            StartDate, EndDate,
            StrictnessRule.Locked, new DailyFloor(0), null, false,
            FullyPlanned(), SealedOnStart);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_EveryDayMeetsFloor_Succeeds()
    {
        var act = () => Seal(dailyFloor: new DailyFloor(4), plannedFocusHoursByDay: FullyPlanned(4));

        act.Should().NotThrow();
    }

    [Fact]
    public void Seal_OneDayBelowFloor_Throws()
    {
        var plan = FullyPlanned(4);
        plan[StartDate.AddDays(3)] = 2; // below the floor of 4

        var act = () => Seal(dailyFloor: new DailyFloor(4), plannedFocusHoursByDay: plan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_DayMissingFromMap_TreatedAsZeroAndThrows()
    {
        var plan = FullyPlanned(4);
        plan.Remove(StartDate.AddDays(2));

        var act = () => Seal(dailyFloor: new DailyFloor(4), plannedFocusHoursByDay: plan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Seal_FloorDisabled_NoPlannedHoursRequired()
    {
        var act = () => Seal(dailyFloor: DailyFloor.None, plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

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
    public void Break_WhenNotActive_Throws()
    {
        var contract = Seal(sealedAt: SealedBeforeStart, allowCancellation: true); // Scheduled

        var act = () => contract.Break(SealedOnStart);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(3.9, false)] // below floor -> not met
    [InlineData(4, true)]    // exactly at floor -> met
    [InlineData(5, true)]    // above floor -> met
    public void IsDayMet_ComparesHoursAgainstFloor(double completedHours, bool expectedMet)
    {
        var contract = Seal(dailyFloor: new DailyFloor(4));

        contract.IsDayMet(completedHours).Should().Be(expectedMet);
    }

    [Fact]
    public void IsDayMet_MetEvenWhenABlockWasMissed_AsLongAsHoursMeetFloor()
    {
        // The day verdict is hours-only now — a missed/late block only costs hours
        // that must be made up elsewhere; it does not fail the day directly.
        var contract = Seal(dailyFloor: new DailyFloor(4));

        contract.IsDayMet(completedFocusHours: 4).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IsDayMet_FloorlessContract_AlwaysMet(double completedHours)
    {
        var contract = Seal(dailyFloor: DailyFloor.None, plannedFocusHoursByDay: new Dictionary<DateOnly, double>());

        contract.IsDayMet(completedHours).Should().BeTrue();
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
        var contract = Seal(dailyFloor: new DailyFloor(4), sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var plan = PlannedForNewDays(EndDate.AddDays(1), newEndDate, 4);
        plan[newEndDate] = 1; // below the floor of 4

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extend_NewDayMissingFromMap_TreatedAsZeroAndThrows()
    {
        var contract = Seal(dailyFloor: new DailyFloor(4), sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var plan = PlannedForNewDays(EndDate.AddDays(1), newEndDate.AddDays(-1), 4); // missing newEndDate itself

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Extend_DoesNotReCheckExistingDays()
    {
        // Existing days were already validated at Seal; Extend only floor-checks the new days.
        var contract = Seal(dailyFloor: new DailyFloor(4), sealedAt: SealedOnStart);
        var newEndDate = EndDate.AddDays(2);
        var plan = PlannedForNewDays(EndDate.AddDays(1), newEndDate, 4);

        var act = () => contract.Extend(newEndDate, plan, SealedOnStart.AddDays(2));

        act.Should().NotThrow();
    }
}
