using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.Services;

/// <summary>Gap 88: the one "is this time free for Focus?" rule.</summary>
public class BlockPlacementCheckerTests
{
    private static readonly DateTime Day = new(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Moment Now = At.Utc(Day.AddHours(8));

    private readonly BlockPlacementChecker _checker = new();

    private static TimeSlot Slot(double fromHour, double toHour) => new(At.Utc(Day.AddHours(fromHour)), At.Utc(Day.AddHours(toHour)));

    private static FocusBlock Focus(double fromHour, double toHour) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Day.AddHours(fromHour), Day.AddHours(toHour)));

    private static BackgroundBlock Background(double fromHour, double toHour) =>
        BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Day.AddHours(fromHour), Day.AddHours(toHour)));

    private static Session RunningSession(double fromHour, double toHour)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromHours(toHour - fromHour)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Day.AddHours(fromHour)));
        return session;
    }

    private PlacementVerdict CheckFree(TimeSlot slot, PlannedBlock[]? blocks = null, Session? running = null) =>
        _checker.CheckFree(slot, blocks ?? [], running, Now);

    [Fact]
    public void EmptyCalendar_IsAllowed()
        => CheckFree(Slot(10, 11)).Should().Be(PlacementVerdict.Allowed);

    [Fact]
    public void FocusBlockInTheWay_IsTimeTaken_WithItsSlot()
    {
        var verdict = CheckFree(Slot(10, 11), [Focus(10.5, 12)]);

        verdict.Outcome.Should().Be(PlacementOutcome.TimeTaken);
        verdict.Conflict.Should().Be(Slot(10.5, 12));
    }

    [Fact]
    public void BackToBackFocusBlock_IsAllowed()
        => CheckFree(Slot(10, 11), [Focus(11, 12), Focus(9, 10)]).Should().Be(PlacementVerdict.Allowed);

    [Fact]
    public void BackgroundBlocks_NeverClash()
        => CheckFree(Slot(10, 11), [Background(9, 12)]).Should().Be(PlacementVerdict.Allowed);

    [Fact]
    public void RunningSessionInTheWay_IsTimeTaken_WithItsSlot()
    {
        var verdict = CheckFree(Slot(8.5, 9.5), running: RunningSession(8, 9));

        verdict.Outcome.Should().Be(PlacementOutcome.TimeTaken);
        verdict.Conflict.Should().Be(Slot(8, 9));
    }

    [Fact]
    public void FinishedSession_IsNotInTheWay()
    {
        var session = RunningSession(7, 9);
        session.Complete(At.Utc(Day.AddHours(7.5)));

        CheckFree(Slot(8, 9), running: session).Should().Be(PlacementVerdict.Allowed);
    }

    [Fact]
    public void FreedTailOfAnEarlyEndedBlock_IsAllowed()
    {
        // A Focus block 7–9 whose session ended at 7:30 only occupies 7–7:30 now.
        var block = Focus(7, 9);
        block.MarkRitualCompleted(At.Utc(Day.AddHours(7)));
        block.MarkSessionEnded(At.Utc(Day.AddHours(7.5)));

        CheckFree(Slot(8, 9), [block]).Should().Be(PlacementVerdict.Allowed);
        CheckFree(Slot(7, 8), [block]).Conflict.Should().Be(Slot(7, 7.5));
    }

    [Fact]
    public void MissedBlockWhoseWindowClosed_FreesItsWholeWindow()
    {
        // Ritual never done, window over: the block occupies nothing (moved from the repository's
        // GetOverlappingAsync tests, Gap 88).
        var block = Focus(6, 7);

        _checker.CheckFree(Slot(6, 7), [block], null, At.Utc(Day.AddHours(7.5))).Should().Be(PlacementVerdict.Allowed);
    }

    [Fact]
    public void PendingBlockAwaitingItsRitual_IsStillInTheWay()
    {
        var block = Focus(6, 7);

        _checker.CheckFree(Slot(6, 7), [block], null, At.Utc(Day.AddHours(6.2))).Conflict.Should().Be(Slot(6, 7));
    }

    [Fact]
    public void CancelledBlock_IsNotInTheWay()
    {
        var block = Focus(10, 11);
        block.MarkCancelled(At.Utc(Day.AddHours(8)));

        CheckFree(Slot(10, 11), [block]).Should().Be(PlacementVerdict.Allowed);
    }

    [Fact]
    public void SeveralInTheWay_ReportsTheEarliest()
        => CheckFree(Slot(10, 14), [Focus(12, 13), Focus(10.5, 11)]).Conflict.Should().Be(Slot(10.5, 11));

    // ── Which contract governs (Gap 97) ──────────────────────────────────────
    // Day is the first of three contract days, each 09:00–17:00; "now" is 08:00 on Day.

    private static NenContract Contract(DateOnly? firstDay = null) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(firstDay ?? DateOnly.FromDateTime(Day), (firstDay ?? DateOnly.FromDateTime(Day)).AddDays(2),
            new DailyFloorRule.None(), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0))),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: Now, timeZone: At.UtcZone);

    private PlacementVerdict Check(TimeSlot slot, ContractMembership? membership, params NenContract[] contracts) =>
        _checker.Check(slot, BlockPresetType.Focus, membership, contracts, [], null, Now, At.UtcZone);

    [Fact]
    public void Check_SlotAlreadyStarted_IsInThePast()
        => Check(Slot(7, 9), null).Outcome.Should().Be(PlacementOutcome.InThePast);

    [Fact]
    public void Check_PlainBlock_InsideThePeriodAndTheDaysWindow_IsAllowedUnderThatContract()
    {
        var contract = Contract();

        var verdict = Check(Slot(10, 11), null, contract);

        verdict.Outcome.Should().Be(PlacementOutcome.Allowed);
        verdict.GoverningContractId.Should().Be(contract.Id);
    }

    [Fact]
    public void Check_PlainBlock_InsideThePeriodButOutsideADaysWindow_IsRefused()
        // 19:00 on the second day: the contract is running, Work Hours are over.
        => Check(Slot(24 + 19, 24 + 20), null, Contract()).Outcome.Should().Be(PlacementOutcome.OutsideContractHours);

    [Theory]
    [InlineData(8.5, 9.5)]            // straddles the period's start (day 1, 09:00)
    [InlineData(48 + 16.5, 48 + 17.5)] // straddles the period's end (day 3, 17:00)
    public void Check_PlainBlock_StraddlingThePeriodsStartOrEnd_IsRefused(double from, double to)
        => Check(Slot(from, to), null, Contract()).Outcome.Should().Be(PlacementOutcome.OutsideContractHours);

    [Theory]
    [InlineData(8.25, 9)]             // day 1, entirely before the period starts
    [InlineData(48 + 19, 48 + 20)]    // day 3, entirely after the period ends
    [InlineData(96 + 10, 96 + 11)]    // two days after the contract
    public void Check_PlainBlock_EntirelyOutsideThePeriod_IsAPlainAllowedBlock(double from, double to)
    {
        var verdict = Check(Slot(from, to), null, Contract());

        verdict.Should().Be(PlacementVerdict.Allowed);
        verdict.GoverningContractId.Should().BeNull();
    }

    [Fact]
    public void Check_FinishedContract_GovernsNothing()
    {
        var broken = Contract();
        broken.Break(Now, At.UtcZone, sessionRunning: false);

        Check(Slot(19, 20), null, broken).Should().Be(PlacementVerdict.Allowed);
    }

    // A block with a contract id is judged against its own contract — every role (Gap 106, decision 17).
    public static TheoryData<string> EveryRole => new() { "Committed", "Redemption", "Bonus" };

    private static ContractMembership MembershipOf(string role, Guid contractId) => role switch
    {
        "Committed" => TestContracts.Committed(contractId),
        "Redemption" => TestContracts.Redemption(contractId),
        _ => TestContracts.Bonus(contractId)
    };

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void Check_AMember_NeverLeavesItsContract(string role)
    {
        var contract = Contract();

        Check(Slot(48 + 19, 48 + 20), MembershipOf(role, contract.Id), contract).Outcome   // after its period
            .Should().Be(PlacementOutcome.OutsideContractHours);
        Check(Slot(96 + 10, 96 + 11), MembershipOf(role, contract.Id), contract).Outcome   // days after it
            .Should().Be(PlacementOutcome.OutsideContractHours);
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void Check_AMember_IsHeldToItsOwnContract_NotTheOneTheSlotSitsIn(string role)
    {
        var own = Contract();
        var next = Contract(DateOnly.FromDateTime(Day).AddDays(5));

        Check(Slot(120 + 10, 120 + 11), MembershipOf(role, own.Id), own, next).Outcome
            .Should().Be(PlacementOutcome.OutsideContractHours);
    }

    [Fact]
    public void Check_CommittedBlock_InsideItsContract_IsAllowedUnderIt()
    {
        var contract = Contract();

        Check(Slot(24 + 10, 24 + 11), TestContracts.Committed(contract.Id), contract).GoverningContractId.Should().Be(contract.Id);
    }

    [Fact]
    public void Check_BonusBlock_InsideItsContract_FollowsItsHours()
    {
        var contract = Contract();
        var bonus = TestContracts.Bonus(contract.Id);

        Check(Slot(24 + 10, 24 + 11), bonus, contract).GoverningContractId.Should().Be(contract.Id);
        Check(Slot(24 + 19, 24 + 20), bonus, contract).Outcome.Should().Be(PlacementOutcome.OutsideContractHours);
    }

    [Fact]
    public void Check_TimeTaken_WinsAfterTheContractAllows()
    {
        var verdict = _checker.Check(Slot(10, 11), BlockPresetType.Focus, null, [Contract()], [Focus(10.5, 12)], null, Now, At.UtcZone);

        verdict.Outcome.Should().Be(PlacementOutcome.TimeTaken);
        verdict.GoverningContractId.Should().BeNull();
    }

    [Fact]
    public void Check_BackgroundBlock_IsHeldToContractHoursButNeverClashes()
    {
        var contract = Contract();

        _checker.Check(Slot(10, 11), BlockPresetType.Background, null, [contract], [Focus(10, 11)], null, Now, At.UtcZone)
            .GoverningContractId.Should().Be(contract.Id);
        _checker.Check(Slot(24 + 19, 24 + 20), BlockPresetType.Background, null, [contract], [], null, Now, At.UtcZone)
            .Outcome.Should().Be(PlacementOutcome.OutsideContractHours);
    }

    [Fact]
    public void Verdict_ConflictExactlyWhenTimeTaken()
    {
        var withoutConflict = () => PlacementVerdict.Refused(PlacementOutcome.TimeTaken);
        var withStrayConflict = () => PlacementVerdict.Refused(PlacementOutcome.InThePast, Slot(10, 11));
        var refusedAllowed = () => PlacementVerdict.Refused(PlacementOutcome.Allowed);

        withoutConflict.Should().Throw<ArgumentException>();
        withStrayConflict.Should().Throw<ArgumentException>();
        refusedAllowed.Should().Throw<ArgumentException>();
    }
}
