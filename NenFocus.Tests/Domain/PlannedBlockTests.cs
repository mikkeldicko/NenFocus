using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class PlannedBlockTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);

    private static FocusBlock CreateFocusBlock(DateTime? start = null, DateTime? end = null)
        => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(start ?? Start, end ?? End));

    private static BackgroundBlock CreateBackgroundBlock(DateTime? start = null, DateTime? end = null)
        => BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(start ?? Start, end ?? End));

    [Fact]
    public void FocusBlock_Create_EndBeforeStart_Throws()
    {
        var act = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(End, Start));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FocusBlock_Create_EndEqualsStart_Throws()
    {
        var act = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FocusBlock_Create_Valid_HasNoRitualCompletionYet()
    {
        var block = CreateFocusBlock();

        block.RitualCompletedAt.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_MarkRitualCompleted_SetsTimestamp()
    {
        var block = CreateFocusBlock();
        var completedAt = DateTime.UtcNow;

        block.MarkRitualCompleted(At.Utc(completedAt));

        block.RitualCompletedAt.Should().Be(At.Utc(completedAt));
    }

    [Fact]
    public void FocusBlock_MarkRitualCompleted_Twice_Throws()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(DateTime.UtcNow));

        var act = () => block.MarkRitualCompleted(At.Utc(DateTime.UtcNow));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FocusBlock_Create_Valid_HasNoSessionEndedYet()
    {
        CreateFocusBlock().SessionEndedAt.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_MarkSessionEnded_SetsTimestamp()
    {
        var block = CreateFocusBlock();
        var endedAt = Start.AddMinutes(20);

        block.MarkSessionEnded(At.Utc(endedAt));

        block.SessionEndedAt.Should().Be(At.Utc(endedAt));
    }

    [Fact]
    public void FocusBlock_MarkSessionEnded_Twice_Throws()
    {
        var block = CreateFocusBlock();
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        var act = () => block.MarkSessionEnded(At.Utc(Start.AddMinutes(30)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FocusBlock_MarkSessionEnded_DoesNotMutateEnd()
    {
        var block = CreateFocusBlock();

        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public void EffectiveEnd_PendingBlock_ReturnsEnd()
        => CreateFocusBlock().EffectiveEnd(At.Utc(Start.AddMinutes(10))).Should().Be(At.Utc(End));

    [Fact]
    public void EffectiveEnd_RitualCompleted_StillRunning_ReturnsEnd()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));

        block.EffectiveEnd(At.Utc(Start.AddMinutes(30))).Should().Be(At.Utc(End));
    }

    [Fact]
    public void EffectiveEnd_SessionEndedEarly_ReturnsSessionEndedAt()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));
        var endedEarly = Start.AddMinutes(20);
        block.MarkSessionEnded(At.Utc(endedEarly));

        block.EffectiveEnd(At.Utc(endedEarly.AddMinutes(5))).Should().Be(At.Utc(endedEarly));
    }

    [Fact]
    public void EffectiveEnd_RanFullDuration_NoSessionEndedStamp_ReturnsEnd()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));

        block.EffectiveEnd(At.Utc(End.AddMinutes(5))).Should().Be(At.Utc(End));
    }

    [Fact]
    public void EffectiveEnd_FullyMissed_WindowAlreadyClosed_ReturnsStart_WholeWindowFreed()
        => CreateFocusBlock().EffectiveEnd(At.Utc(End.AddMinutes(1))).Should().Be(At.Utc(Start));

    [Fact]
    public void EffectiveEnd_FullyMissed_ExactlyAtEnd_ReturnsStart()
        => CreateFocusBlock().EffectiveEnd(At.Utc(End)).Should().Be(At.Utc(Start));

    [Fact]
    public void EffectiveEnd_NotYetMissed_RitualStillPending_ReturnsEnd()
        => CreateFocusBlock().EffectiveEnd(At.Utc(End.AddMinutes(-1))).Should().Be(At.Utc(End));

    [Fact]
    public void EffectiveEnd_DoesNotMutateEnd()
    {
        var block = CreateFocusBlock();

        block.EffectiveEnd(At.Utc(End.AddMinutes(1)));

        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public void BackgroundBlock_EffectiveEnd_AlwaysReturnsEnd()
        => CreateBackgroundBlock().EffectiveEnd(At.Utc(End.AddYears(1))).Should().Be(At.Utc(End));

    [Fact]
    public void BackgroundBlock_Create_EndBeforeStart_Throws()
    {
        var act = () => BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(End, Start));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BackgroundBlock_Create_Valid_SetsWindow()
    {
        var block = CreateBackgroundBlock();

        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Theory]
    [InlineData(9, 30, 10, 30, true)]   // overlaps start
    [InlineData(10, 30, 11, 30, true)]  // overlaps end
    [InlineData(10, 15, 10, 45, true)]  // fully contained
    [InlineData(8, 0, 9, 0, false)]     // strictly before
    [InlineData(11, 0, 12, 0, false)]   // strictly after
    [InlineData(8, 0, 10, 0, false)]    // touches start boundary only
    public void OverlapsWith_VariousRanges(int otherStartHour, int otherStartMinute,
        int otherEndHour, int otherEndMinute, bool expectedOverlap)
    {
        var block = CreateFocusBlock();
        var other = CreateBackgroundBlock(
            new DateTime(2026, 1, 1, otherStartHour, otherStartMinute, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, otherEndHour, otherEndMinute, 0, DateTimeKind.Utc));

        block.OverlapsWith(other).Should().Be(expectedOverlap);
    }

    [Fact]
    public void OverlapsWith_TouchingEndBoundary_IsNotAnOverlap()
    {
        var block = CreateFocusBlock();
        var adjacent = CreateBackgroundBlock(End, End.AddHours(1));

        block.OverlapsWith(adjacent).Should().BeFalse();
    }

    [Fact]
    public void IsActive_AtStart_IsTrue()
        => CreateFocusBlock().IsActive(At.Utc(Start)).Should().BeTrue();

    [Fact]
    public void IsActive_AtEnd_IsFalse()
        => CreateFocusBlock().IsActive(At.Utc(End)).Should().BeFalse();

    [Fact]
    public void IsActive_BeforeStart_IsFalse()
        => CreateFocusBlock().IsActive(At.Utc(Start.AddMinutes(-1))).Should().BeFalse();

    [Fact]
    public void IsActive_MidWindow_IsTrue()
        => CreateFocusBlock().IsActive(At.Utc(Start.AddMinutes(30))).Should().BeTrue();

    [Fact]
    public void IsActive_SessionEndedEarly_AfterActualEnd_IsFalse()
    {
        // The bug this guards: IsActive used to compare against raw End, so a block whose
        // session ended early still looked "active" all the way to its original scheduled
        // end — letting PolicyEngine pick a stale, already-resolved block over a genuinely
        // new one scheduled into the freed tail, permanently suppressing the new block's
        // Ritual trigger.
        var block = CreateFocusBlock();
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        block.IsActive(At.Utc(Start.AddMinutes(21))).Should().BeFalse();
    }

    [Fact]
    public void IsActive_SessionEndedEarly_BeforeActualEnd_IsStillTrue()
    {
        var block = CreateFocusBlock();
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        block.IsActive(At.Utc(Start.AddMinutes(10))).Should().BeTrue();
    }

    [Fact]
    public void HasStarted_BeforeStart_IsFalse()
        => CreateFocusBlock().HasStarted(At.Utc(Start.AddMinutes(-1))).Should().BeFalse();

    [Fact]
    public void HasStarted_AtStart_IsTrue()
        => CreateFocusBlock().HasStarted(At.Utc(Start)).Should().BeTrue();

    [Fact]
    public void HasStarted_AfterEnd_IsTrue()
        => CreateBackgroundBlock().HasStarted(At.Utc(End.AddMinutes(1))).Should().BeTrue();

    [Fact]
    public void HasEnded_BeforeEnd_IsFalse()
        => CreateFocusBlock().HasEnded(At.Utc(End.AddMinutes(-1))).Should().BeFalse();

    [Fact]
    public void HasEnded_AtEnd_IsTrue()
        => CreateFocusBlock().HasEnded(At.Utc(End)).Should().BeTrue("half-open: at exactly End the block is over (Gap 88)");

    [Fact]
    public void AtTheExactEnd_IsActiveHasEndedAndMissedAgree()
    {
        // Gap 88 parity: these three used to disagree at the end instant (IsActive: not active,
        // HasEnded: not ended, Classify: missed). One boundary rule — the slot's — now answers all three.
        var block = CreateFocusBlock();

        block.IsActive(At.Utc(End)).Should().BeFalse();
        block.HasEnded(At.Utc(End)).Should().BeTrue();
        new NenFocus.Domain.Services.FocusBlockOutcomeClassifier().Classify(block, At.Utc(End)).Should().Be(FocusBlockOutcome.Missed);
        block.IsUnresolved(At.Utc(End)).Should().BeTrue();
    }

    [Fact]
    public void HasEnded_AfterEnd_IsTrue()
        => CreateFocusBlock().HasEnded(At.Utc(End.AddMinutes(1))).Should().BeTrue();

    [Fact]
    public void GetDuration_ReturnsEndMinusStart()
        => CreateFocusBlock().GetDuration().Should().Be(TimeSpan.FromHours(1));

    // Block 10:00–11:00; warning moment = Start − 5 min = 09:55.

    private static IEnumerable<BlockMarkerKind> Kinds(PlannedBlock block, DateTime from, DateTime to)
        => block.MarkersPassedBetween(At.Utc(from), At.Utc(to)).Select(t => t.Kind);

    [Fact]
    public void MarkersPassedBetween_ClockCrossesWarningMoment_IsStartingSoon()
        => Kinds(CreateFocusBlock(), Start.AddMinutes(-6), Start.AddMinutes(-5))
            .Should().Equal(BlockMarkerKind.StartingSoon);

    [Fact]
    public void MarkersPassedBetween_WarningMomentNotReachedYet_IsNothing()
        => Kinds(CreateFocusBlock(), Start.AddMinutes(-7), Start.AddMinutes(-6)).Should().BeEmpty();

    [Fact]
    public void MarkersPassedBetween_WarningAlreadyCrossedEarlier_IsNothing()
        => Kinds(CreateFocusBlock(), Start.AddMinutes(-5), Start.AddMinutes(-4)).Should().BeEmpty();

    [Fact]
    public void MarkersPassedBetween_ClockCrossesStart_IsStarted()
        => Kinds(CreateBackgroundBlock(), Start.AddSeconds(-5), Start)
            .Should().Equal(BlockMarkerKind.Started);

    [Fact]
    public void MarkersPassedBetween_ClockJumpsFromBeforeWarningPastStart_IsStartedOnly()
        => Kinds(CreateFocusBlock(), Start.AddMinutes(-6), Start.AddMinutes(1))
            .Should().Equal(BlockMarkerKind.Started);

    [Fact]
    public void MarkersPassedBetween_BackgroundBlockCrossesEnd_IsEnded()
        => Kinds(CreateBackgroundBlock(), End.AddSeconds(-5), End)
            .Should().Equal(BlockMarkerKind.Ended);

    [Fact]
    public void MarkersPassedBetween_FocusBlockWithoutRitualCrossesEnd_IsEnded()
        => Kinds(CreateFocusBlock(), End.AddSeconds(-5), End)
            .Should().Equal(BlockMarkerKind.Ended);

    [Fact]
    public void MarkersPassedBetween_FocusBlockWhoseSessionRan_NeverAnnouncesItsOwnEnd()
    {
        // Its session owns the ending and announces it (SessionEndedEvent).
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));

        Kinds(block, End.AddSeconds(-5), End).Should().BeEmpty();
    }

    [Fact]
    public void MarkersPassedBetween_FocusBlockWhoseSessionEndedEarly_StaysQuietAtEitherEnd()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(40)));

        Kinds(block, Start.AddMinutes(39), Start.AddMinutes(41)).Should().BeEmpty();
        Kinds(block, End.AddSeconds(-5), End).Should().BeEmpty();
    }

    [Fact]
    public void MarkersPassedBetween_ClockJumpsOverWholeBlock_IsNothing()
        => Kinds(CreateBackgroundBlock(), Start.AddMinutes(-1), End.AddMinutes(1)).Should().BeEmpty();

    [Fact]
    public void MarkersPassedBetween_ClockMovedBackwards_IsNothing()
        => Kinds(CreateBackgroundBlock(), Start.AddMinutes(1), Start.AddMinutes(-6)).Should().BeEmpty();

    [Fact]
    public void MarkersPassedBetween_CancelledBlock_IsNothing()
    {
        var block = CreateBackgroundBlock();
        block.MarkCancelled(At.Utc(Start.AddHours(-1)));

        Kinds(block, Start.AddMinutes(-6), Start.AddMinutes(1)).Should().BeEmpty();
    }

    [Fact]
    public void MarkersPassedBetween_CancelledMidRun_EndsAtTheCancelMoment()
    {
        var block = CreateBackgroundBlock();
        var cancelledAt = Start.AddMinutes(30);
        block.MarkCancelled(At.Utc(cancelledAt));

        block.MarkersPassedBetween(At.Utc(cancelledAt.AddSeconds(-5)), At.Utc(cancelledAt))
            .Should().Equal(new BlockMarker(BlockMarkerKind.Ended, cancelledAt));
    }

    // Cancellation is one of the reasons a block's reserved window shrinks (EffectiveEnd),
    // so every question built on that window follows without checking CancelledAt itself.

    [Fact]
    public void IsActive_CancelledBeforeStart_IsNeverActive()
    {
        var block = CreateBackgroundBlock();
        block.MarkCancelled(At.Utc(Start.AddHours(-1)));

        block.IsActive(At.Utc(Start)).Should().BeFalse();
        block.IsActive(At.Utc(Start.AddMinutes(30))).Should().BeFalse();
        block.EffectiveEnd(At.Utc(Start.AddMinutes(30))).Should().Be(At.Utc(Start));
    }

    [Fact]
    public void IsActive_CancelledMidRun_ActiveUntilTheCancelMomentOnly()
    {
        var block = CreateBackgroundBlock();
        var cancelledAt = Start.AddMinutes(30);
        block.MarkCancelled(At.Utc(cancelledAt));

        block.IsActive(At.Utc(Start.AddMinutes(10))).Should().BeTrue();
        block.IsActive(At.Utc(cancelledAt)).Should().BeFalse();
        block.EffectiveEnd(At.Utc(End)).Should().Be(At.Utc(cancelledAt));
    }

    [Fact]
    public void EffectiveEnd_FocusBlock_EarliestReasonWins()
    {
        var endedFirst = CreateFocusBlock();
        endedFirst.MarkRitualCompleted(At.Utc(Start));
        endedFirst.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));
        endedFirst.MarkCancelled(At.Utc(Start.AddMinutes(40)));

        var cancelledFirst = CreateFocusBlock();
        cancelledFirst.MarkRitualCompleted(At.Utc(Start));
        cancelledFirst.MarkCancelled(At.Utc(Start.AddMinutes(20)));
        cancelledFirst.MarkSessionEnded(At.Utc(Start.AddMinutes(40)));

        endedFirst.EffectiveEnd(At.Utc(End)).Should().Be(At.Utc(Start.AddMinutes(20)));
        cancelledFirst.EffectiveEnd(At.Utc(End)).Should().Be(At.Utc(Start.AddMinutes(20)));
    }

    [Fact]
    public void DisplayName_NoTitle_IsThePresetName()
        => CreateFocusBlock().DisplayName("Deep Work").Should().Be("Deep Work");

    [Fact]
    public void DisplayName_WithTitle_IsTheTitle()
        => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), "Thesis")
            .DisplayName("Deep Work").Should().Be("Thesis");

    [Fact]
    public void PresetType_FollowsTheBlockKind()
    {
        CreateFocusBlock().PresetType.Should().Be(BlockPresetType.Focus);
        CreateBackgroundBlock().PresetType.Should().Be(BlockPresetType.Background);
    }

    [Fact]
    public void Reschedule_Valid_UpdatesStartAndEnd()
    {
        var block = CreateFocusBlock();
        var newStart = Start.AddHours(2);
        var newEnd = End.AddHours(2);

        block.Reschedule(At.Slot(newStart, newEnd));

        block.Scheduled.Should().Be(At.Slot(newStart, newEnd));
    }

    [Fact]
    public void Reschedule_EndBeforeStart_ThrowsAndLeavesWindowUnchanged()
    {
        var block = CreateFocusBlock();

        var act = () => block.Reschedule(At.Slot(End, Start));

        act.Should().Throw<ArgumentException>();
        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public void FocusBlock_Create_ExceedsMaxDuration_Throws()
    {
        var act = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(FocusBlock.MaxDurationHours).AddMinutes(1)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FocusBlock_Create_AtExactlyMaxDuration_Succeeds()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(FocusBlock.MaxDurationHours)));

        block.GetDuration().Should().Be(TimeSpan.FromHours(FocusBlock.MaxDurationHours));
    }

    [Fact]
    public void BackgroundBlock_Create_ExceedsFocusMaxDuration_Succeeds()
    {
        // Background blocks have no duration cap at all — Focus-only by design.
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(FocusBlock.MaxDurationHours).AddDays(3)));

        block.GetDuration().Should().Be(TimeSpan.FromHours(FocusBlock.MaxDurationHours).Add(TimeSpan.FromDays(3)));
    }

    [Fact]
    public void FocusBlock_Reschedule_ExceedsMaxDuration_ThrowsAndLeavesWindowUnchanged()
    {
        var block = CreateFocusBlock();

        var act = () => block.Reschedule(At.Slot(Start, Start.AddHours(FocusBlock.MaxDurationHours).AddMinutes(1)));

        act.Should().Throw<ArgumentException>();
        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public void FocusBlock_Reschedule_AtExactlyMaxDuration_Succeeds()
    {
        var block = CreateFocusBlock();
        var newEnd = Start.AddHours(FocusBlock.MaxDurationHours);

        block.Reschedule(At.Slot(Start, newEnd));

        block.Scheduled.Should().Be(At.Slot(Start, newEnd));
    }

    [Fact]
    public void BackgroundBlock_Reschedule_ExceedsFocusMaxDuration_Succeeds()
    {
        var block = CreateBackgroundBlock();
        var newEnd = Start.AddHours(FocusBlock.MaxDurationHours).AddDays(3);

        block.Reschedule(At.Slot(Start, newEnd));

        block.Scheduled.Should().Be(At.Slot(Start, newEnd));
    }

    [Fact]
    public void TrimEnd_Valid_UpdatesEnd()
    {
        var block = CreateFocusBlock();
        var newEnd = Start.AddMinutes(45);

        block.Reschedule(block.Scheduled.Until(At.Utc(newEnd)));

        block.Scheduled.Should().Be(At.Slot(Start, newEnd));
    }

    [Fact]
    public void TrimEnd_BeforeOrAtStart_Throws()
    {
        var block = CreateFocusBlock();

        var act = () => block.Reschedule(block.Scheduled.Until(At.Utc(Start)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignPreset_UpdatesBlockPresetId()
    {
        var block = CreateFocusBlock();
        var newPresetId = Guid.NewGuid();

        block.AssignPreset(newPresetId);

        block.BlockPresetId.Should().Be(newPresetId);
    }

    [Fact]
    public void FocusBlock_Create_WithoutMembership_IsAPlainBlock()
    {
        CreateFocusBlock().Membership.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_Create_WithMembership_SetsIt()
    {
        var contractId = Guid.NewGuid();

        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), membership: TestContracts.Committed(contractId));

        block.Membership!.ContractId.Should().Be(contractId);
    }

    [Fact]
    public void BackgroundBlock_Create_WithoutMembership_IsAPlainBlock()
    {
        CreateBackgroundBlock().Membership.Should().BeNull();
    }

    [Fact]
    public void BackgroundBlock_Create_WithMembership_SetsIt()
    {
        var contractId = Guid.NewGuid();

        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), membership: TestContracts.Committed(contractId));

        block.Membership!.ContractId.Should().Be(contractId);
    }

    // ── Membership in a contract (Gap 97) ────────────────────────────────────

    [Fact]
    public void Membership_OnlyCommittedAndRedemptionCountTowardTheFloor()
    {
        var contractId = Guid.NewGuid();

        TestContracts.Committed(contractId).CountsTowardFloor.Should().BeTrue();
        TestContracts.Redemption(contractId).CountsTowardFloor.Should().BeTrue();
        TestContracts.Bonus(contractId).CountsTowardFloor.Should().BeFalse();
    }

    [Fact]
    public void Membership_NamesItsRole()
    {
        var contractId = Guid.NewGuid();

        TestContracts.Redemption(contractId).IsRedemption.Should().BeTrue();
        TestContracts.Redemption(contractId).IsBonus.Should().BeFalse();
        TestContracts.Bonus(contractId).IsBonus.Should().BeTrue();
        TestContracts.Committed(contractId).IsRedemption.Should().BeFalse();
        TestContracts.Committed(contractId).IsBonus.Should().BeFalse();
    }

    [Fact]
    public void Membership_SameContractDifferentRole_IsNotTheSameMembership()
    {
        var contractId = Guid.NewGuid();

        TestContracts.Bonus(contractId).Should().Be(TestContracts.Bonus(contractId));
        TestContracts.Bonus(contractId).Should().NotBe(TestContracts.Committed(contractId));
    }

    [Fact]
    public void Join_PlainBlock_BecomesAMember()
    {
        var block = CreateFocusBlock();
        var membership = TestContracts.Bonus(Guid.NewGuid());

        block.Join(membership);

        block.Membership.Should().Be(membership);
    }

    [Fact]
    public void Join_AlreadyAMember_Throws()
    {
        var block = CreateFocusBlock();
        block.Join(TestContracts.Bonus(Guid.NewGuid()));

        var act = () => block.Join(TestContracts.Bonus(Guid.NewGuid()));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Leave_BonusBlock_BecomesAPlainBlock()
    {
        var block = CreateFocusBlock();
        block.Join(TestContracts.Bonus(Guid.NewGuid()));

        block.Leave();

        block.Membership.Should().BeNull();
    }

    [Fact]
    public void Leave_CommittedOrRedemptionBlock_Throws()
    {
        var contractId = Guid.NewGuid();
        var committed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(contractId));
        var redemption = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Redemption(contractId));

        ((Action)committed.Leave).Should().Throw<InvalidOperationException>();
        ((Action)redemption.Leave).Should().Throw<InvalidOperationException>();
    }

    private static NenContract ContractInEffect() => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(Start.Date), timeZone: At.UtcZone);

    [Fact]
    public void PlaceUnder_PlainBlock_UnderAContract_BecomesItsBonusBlock()
    {
        var block = CreateFocusBlock();
        var contract = ContractInEffect();

        block.PlaceUnder(contract);

        block.Membership.Should().Be(TestContracts.Bonus(contract.Id));
    }

    [Fact]
    public void PlaceUnder_BonusBlock_KeepsItsMembershipWhateverGoverns()
    {
        // No member leaves its contract by being moved (Gap 93) — only the contract releases it.
        var contract = ContractInEffect();
        var underNone = CreateFocusBlock();
        var underAnother = CreateFocusBlock();
        underNone.PlaceUnder(contract);
        underAnother.PlaceUnder(contract);

        underNone.PlaceUnder(null);
        underAnother.PlaceUnder(ContractInEffect());

        underNone.Membership.Should().Be(TestContracts.Bonus(contract.Id));
        underAnother.Membership.Should().Be(TestContracts.Bonus(contract.Id));
    }

    [Fact]
    public void PlaceUnder_CommittedOrRedemptionBlock_KeepsItsMembershipWhateverGoverns()
    {
        var contractId = Guid.NewGuid();
        var committed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(contractId));
        var redemption = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Redemption(contractId));

        committed.PlaceUnder(null);
        redemption.PlaceUnder(ContractInEffect());

        committed.Membership.Should().Be(TestContracts.Committed(contractId));
        redemption.Membership.Should().Be(TestContracts.Redemption(contractId));
    }

    [Fact]
    public void IsRedemption_IsTheRole_NotWhetherTheBlockCoversAnything()
    {
        var contractId = Guid.NewGuid();
        var redemption = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Redemption(contractId));

        redemption.RedemptionEntries.Should().BeEmpty();
        redemption.IsRedemption.Should().BeTrue();
        redemption.CountsTowardFloor.Should().BeTrue();
        redemption.IsBonus.Should().BeFalse();
    }

    [Fact]
    public void RedemptionEntries_AreRefused_UnlessTheBlockIsARedemptionBlock()
    {
        var contractId = Guid.NewGuid();
        RedemptionEntry[] entries = [new(Guid.NewGuid(), 30)];
        var createPlain = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), redemptionEntries: entries);
        var createCommitted = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End),
            membership: TestContracts.Committed(contractId), redemptionEntries: entries);
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Bonus(contractId));
        var assignToBonus = () => bonus.AssignRedemptionEntries(entries);

        createPlain.Should().Throw<ArgumentException>();
        createCommitted.Should().Throw<ArgumentException>();
        assignToBonus.Should().Throw<InvalidOperationException>();
        bonus.AssignRedemptionEntries([]); // clearing is always fine
    }

    [Fact]
    public void Leave_PlainBlock_Throws()
    {
        var act = () => CreateFocusBlock().Leave();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FocusBlock_Create_WithoutRedemptionEntries_IsEmpty()
    {
        CreateFocusBlock().RedemptionEntries.Should().BeEmpty();
    }

    [Fact]
    public void FocusBlock_Create_WithRedemptionEntries_SetsThem()
    {
        var entries = new[] { new RedemptionEntry(Guid.NewGuid(), 30) };

        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), membership: TestContracts.Redemption(Guid.NewGuid()), redemptionEntries: entries);

        block.RedemptionEntries.Should().BeEquivalentTo(entries);
    }

    [Fact]
    public void BackgroundBlock_Create_RedemptionEntries_AlwaysEmpty()
    {
        CreateBackgroundBlock().RedemptionEntries.Should().BeEmpty();
    }

    [Fact]
    public void FocusBlock_AssignRedemptionEntries_ReplacesExisting()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End),
            membership: TestContracts.Redemption(Guid.NewGuid()), redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 30)]);
        var newEntries = new[] { new RedemptionEntry(Guid.NewGuid(), 45), new RedemptionEntry(Guid.NewGuid(), 15) };

        block.AssignRedemptionEntries(newEntries);

        block.RedemptionEntries.Should().BeEquivalentTo(newEntries);
    }

    [Fact]
    public void FocusBlock_AssignRedemptionEntries_Empty_ClearsExisting()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End),
            membership: TestContracts.Redemption(Guid.NewGuid()), redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 30)]);

        block.AssignRedemptionEntries([]);

        block.RedemptionEntries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FocusBlockOutcome.Missed)]
    [InlineData(FocusBlockOutcome.StartedLate)]
    [InlineData(FocusBlockOutcome.EndedEarly)]
    public void FocusBlock_CanContributeOwnDeficit_ShortfallOutcomeNotARedemption_True(FocusBlockOutcome outcome)
    {
        CreateFocusBlock().CanContributeOwnDeficit(outcome).Should().BeTrue();
    }

    [Fact]
    public void FocusBlock_CanContributeOwnDeficit_CompletedOnTime_False()
    {
        CreateFocusBlock().CanContributeOwnDeficit(FocusBlockOutcome.CompletedOnTime).Should().BeFalse();
    }

    [Fact]
    public void FocusBlock_CanContributeOwnDeficit_StillOpen_False()
    {
        CreateFocusBlock().CanContributeOwnDeficit(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(FocusBlockOutcome.Missed)]
    [InlineData(FocusBlockOutcome.StartedLate)]
    [InlineData(FocusBlockOutcome.EndedEarly)]
    public void FocusBlock_CanContributeOwnDeficit_AlreadyARedemption_False(FocusBlockOutcome outcome)
    {
        // A block covering someone else's deficit never becomes a deficit source of its
        // own, no matter how its own completion turned out — this is the exact bug the
        // "Cover Deficit" flow used to hit: a redemption block that ran a few minutes past
        // its own grace period would otherwise spawn a fresh, uncoverable deficit.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End),
            membership: TestContracts.Redemption(Guid.NewGuid()), redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 30)]);

        block.CanContributeOwnDeficit(outcome).Should().BeFalse();
    }

    [Fact]
    public void FocusBlock_Create_Valid_HasNoCancelledAtYet()
    {
        CreateFocusBlock().CancelledAt.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_MarkCancelled_SetsTimestamp()
    {
        var block = CreateFocusBlock();
        var cancelledAt = DateTime.UtcNow;

        block.MarkCancelled(At.Utc(cancelledAt));

        block.CancelledAt.Should().Be(At.Utc(cancelledAt));
    }

    [Fact]
    public void FocusBlock_MarkCancelled_Twice_Throws()
    {
        var block = CreateFocusBlock();
        block.MarkCancelled(At.Utc(DateTime.UtcNow));

        var act = () => block.MarkCancelled(At.Utc(DateTime.UtcNow));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FocusBlock_MarkCancelled_RaisesPlannedBlockCancelledEvent()
    {
        var block = CreateFocusBlock();
        var cancelledAt = DateTime.UtcNow;

        block.MarkCancelled(At.Utc(cancelledAt));

        block.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PlannedBlockCancelledEvent>()
            .Which.Should().Match<PlannedBlockCancelledEvent>(e =>
                e.PlannedBlockId == block.Id && e.OccurredAt == At.Utc(cancelledAt));
    }

    [Fact]
    public void BackgroundBlock_Create_Valid_HasNoCancelledAtYet()
    {
        CreateBackgroundBlock().CancelledAt.Should().BeNull();
    }

    [Fact]
    public void BackgroundBlock_MarkCancelled_SetsTimestamp()
    {
        var block = CreateBackgroundBlock();
        var cancelledAt = DateTime.UtcNow;

        block.MarkCancelled(At.Utc(cancelledAt));

        block.CancelledAt.Should().Be(At.Utc(cancelledAt));
    }

    [Fact]
    public void BackgroundBlock_MarkCancelled_Twice_Throws()
    {
        var block = CreateBackgroundBlock();
        block.MarkCancelled(At.Utc(DateTime.UtcNow));

        var act = () => block.MarkCancelled(At.Utc(DateTime.UtcNow));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BackgroundBlock_MarkCancelled_RaisesPlannedBlockCancelledEvent()
    {
        var block = CreateBackgroundBlock();
        var cancelledAt = DateTime.UtcNow;

        block.MarkCancelled(At.Utc(cancelledAt));

        block.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PlannedBlockCancelledEvent>();
    }
}
