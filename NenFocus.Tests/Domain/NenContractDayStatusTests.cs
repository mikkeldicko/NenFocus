using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

/// <summary>
/// One day-target rule for every contract (Gap 95, decisions 5 and 6): a day owes its Committed
/// blocks' planned hours, plus what the redemptions on it cover (once per missed block per day,
/// never more than that block's deficit). Target 0 is a rest day; met is completed ≥ target.
/// </summary>
public class NenContractDayStatusTests
{
    private static readonly DateOnly Monday = new(2026, 1, 5);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly Moment AfterItAll = At.Utc(2026, 1, 8, 12);

    private static NenContract Contract(StrictnessRule strictness = StrictnessRule.Locked, double? fixedFloorHours = null,
        IReadOnlyDictionary<DateOnly, double>? plan = null)
    {
        var rules = TestRules.Of(strictness, fixedFloorHours is { } hours ? new DailyFloor(hours) : null);
        plan ??= new Dictionary<DateOnly, double>();
        return NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(Monday, Wednesday, rules.FloorRule, plan), plan, At.Utc(2026, 1, 4, 12), At.UtcZone);
    }

    private static TimeSlot Slot(DateOnly day, double fromHour, double toHour) =>
        At.Slot(day.ToDateTime(TimeOnly.MinValue).AddHours(fromHour), day.ToDateTime(TimeOnly.MinValue).AddHours(toHour));

    private static FocusBlock Committed(NenContract contract, TimeSlot slot, Guid? presetId = null) =>
        FocusBlock.Create(Guid.NewGuid(), presetId ?? Guid.NewGuid(), slot, membership: TestContracts.Committed(contract.Id));

    private static FocusBlock Redemption(NenContract contract, TimeSlot slot, FocusBlock missed, long minutesCovered) =>
        FocusBlock.CreateWithReceipt(Guid.NewGuid(), missed.BlockPresetId, slot, title: null, membership: TestContracts.Redemption(contract.Id),
            receipt: new RedemptionReceipt([new RedemptionEntry(missed.Id, WholeMinutes.Of(minutesCovered))]));

    private static ContractDayStatus On(IReadOnlyList<ContractDayStatus> statuses, DateOnly date) =>
        statuses.Single(s => s.Date == date);

    private static IReadOnlyList<ContractDayStatus> Statuses(NenContract contract, IEnumerable<Block> blocks,
        IEnumerable<Session>? sessions = null) =>
        contract.DayStatuses(blocks, sessions ?? [], AfterItAll, At.UtcZone);

    [Fact]
    public void ADayOwesItsCommittedBlocks_AndIsMetWhenItsCreditReachesThem()
    {
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 2, [Tuesday] = 2 });
        var worked = TestBlocks.Worked(Committed(contract, Slot(Monday, 10, 12)), ritualAfterStart: 0, endedAfterStart: 120);
        var missed = Committed(contract, Slot(Tuesday, 10, 12));

        var statuses = Statuses(contract, [worked, missed]);

        On(statuses, Monday).Should().Be(new ContractDayStatus(Monday, 2, 2, 0));
        On(statuses, Tuesday).Should().Be(new ContractDayStatus(Tuesday, 2, 0, 0));
        On(statuses, Wednesday).Should().Be(new ContractDayStatus(Wednesday, 0, 0, 0));
    }

    [Fact]
    public void AFixedFloorDayPlannedAboveItsFloor_OwesWhatWasPlanned_NotTheFloor()
    {
        // Moved number (decision 5): a 2h fixed floor, 3h planned, 2h done. The floor was only the
        // planning minimum; the day owes the 3h signed, so it is not met (it was before).
        var contract = Contract(fixedFloorHours: 2,
            plan: new Dictionary<DateOnly, double> { [Monday] = 3, [Tuesday] = 2, [Wednesday] = 2 });
        var late = TestBlocks.Worked(Committed(contract, Slot(Monday, 9, 12)), ritualAfterStart: 60, endedAfterStart: 180);

        var monday = On(Statuses(contract, [late]), Monday);

        monday.TargetHours.Should().Be(3);
        monday.CompletedHours.Should().Be(2);
        monday.IsMet.Should().BeFalse();
    }

    [Fact]
    public void UnderPeriodStudy_APlannedDay_HasItsOwnTarget()
    {
        // Moved number (decision 5): with no per-day floors every day used to read as rest, so the
        // banner and recap never counted a day behind. Now a planned day owes its plan.
        var contract = Contract(StrictnessRule.PeriodTimeBudget, plan: new Dictionary<DateOnly, double> { [Monday] = 2 });
        var missed = Committed(contract, Slot(Monday, 10, 12));

        var monday = On(Statuses(contract, [missed]), Monday);

        monday.IsRest.Should().BeFalse();
        monday.TargetHours.Should().Be(2);
        monday.IsMet.Should().BeFalse();
    }

    [Fact]
    public void ADayCarryingARedemption_OwesWhatItCovers()
    {
        // Moved number (decision 5): Tuesday had no floor (rest in the recap), but a redemption
        // covering an hour of Monday's miss now makes it owe that hour.
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 2 });
        var missed = Committed(contract, Slot(Monday, 10, 12));
        var redemption = TestBlocks.Worked(Redemption(contract, Slot(Tuesday, 10, 11), missed, minutesCovered: 60),
            ritualAfterStart: 0, endedAfterStart: 60);

        var tuesday = On(Statuses(contract, [missed, redemption]), Tuesday);

        tuesday.IsRest.Should().BeFalse();
        tuesday.TargetHours.Should().Be(1);
        tuesday.CompletedHours.Should().Be(1);
        tuesday.IsMet.Should().BeTrue();
    }

    [Fact]
    public void TwoRedemptionsOfOneMissedBlockTheSameDay_OweNoMoreThanItsDeficit()
    {
        // The first attempt ended early after 20 minutes; the second claims the 40 left. The day owes
        // the missed block's 60, not 60 + 40.
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 1 });
        var missed = Committed(contract, Slot(Monday, 9, 10));
        var first = TestBlocks.Worked(Redemption(contract, Slot(Tuesday, 9, 10), missed, 60), ritualAfterStart: 0, endedAfterStart: 20);
        var second = TestBlocks.Worked(Redemption(contract, Slot(Tuesday, 11, 12), missed, 40), ritualAfterStart: 0, endedAfterStart: 40);

        var tuesday = On(Statuses(contract, [missed, first, second]), Tuesday);

        tuesday.TargetHours.Should().Be(1);
        tuesday.CompletedHours.Should().Be(1); // 20 + 40 minutes
        tuesday.IsMet.Should().BeTrue();
    }

    [Fact]
    public void ACancelledCommittedBlock_StillOwesItsHours()
    {
        // Breaking a contract cancels the rest of the break day, which stays whole: breaking can
        // never clean up a bad day.
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 2 });
        var cancelled = Committed(contract, Slot(Monday, 10, 12));
        cancelled.MarkCancelled(At.Utc(Monday.ToDateTime(new TimeOnly(9, 0))));

        On(Statuses(contract, [cancelled]), Monday).TargetHours.Should().Be(2);
    }

    [Fact]
    public void BonusHours_AreCountedApart()
    {
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 2 });
        var committed = TestBlocks.Worked(Committed(contract, Slot(Monday, 10, 12)), ritualAfterStart: 0, endedAfterStart: 120);
        var bonus = TestBlocks.Worked(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Slot(Monday, 13, 14), membership: contract.MembershipForPlacedBlock()),
            ritualAfterStart: 0, endedAfterStart: 60);

        On(Statuses(contract, [committed, bonus]), Monday)
            .Should().Be(new ContractDayStatus(Monday, 2, 2, 1));
    }

    [Fact]
    public void ASpontaneousSessionOnAContractPreset_CountsAsCompleted_OthersDoNot()
    {
        // Today's rule, until Gap 67 makes such sessions Bonus.
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 2 });
        var presetId = Guid.NewGuid();
        var committed = Committed(contract, Slot(Monday, 10, 12), presetId);
        Session Spontaneous(Guid preset, DateOnly day)
        {
            var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
                preset, SessionOrigin.Spontaneous);
            session.Start(At.Utc(day.ToDateTime(new TimeOnly(15, 0))));
            session.Complete(At.Utc(day.ToDateTime(new TimeOnly(15, 30))), null);
            return session;
        }

        var statuses = Statuses(contract, [committed],
            [Spontaneous(presetId, Monday), Spontaneous(Guid.NewGuid(), Monday), Spontaneous(presetId, Monday.AddDays(-1))]);

        On(statuses, Monday).CompletedHours.Should().Be(0.5);
    }

    [Fact]
    public void PercentMet_IsCompletedOverTarget_AndNoneOnARestDay()
    {
        var contract = Contract(plan: new Dictionary<DateOnly, double> { [Monday] = 2 });
        var half = TestBlocks.Worked(Committed(contract, Slot(Monday, 10, 12)), ritualAfterStart: 0, endedAfterStart: 60);

        var statuses = Statuses(contract, [half]);

        On(statuses, Monday).PercentMet.Should().Be(0.5);
        On(statuses, Wednesday).PercentMet.Should().BeNull();
    }

    [Fact]
    public void ABlockOfAnotherContract_Throws()
    {
        var contract = Contract();
        var foreign = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Slot(Monday, 10, 12), membership: TestContracts.Committed(Guid.NewGuid()));

        var act = () => Statuses(contract, [foreign]);

        act.Should().Throw<ArgumentException>();
    }
}
