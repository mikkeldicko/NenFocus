using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class BlockTests
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
        CreateFocusBlock().IsAwaitingRitual(At.Utc(Start)).Should().BeTrue();
    }

    [Fact]
    public void FocusBlock_MarkRitualCompleted_SetsTimestamp()
    {
        var block = CreateFocusBlock();
        var completedAt = Start.AddMinutes(10); // past grace, so the stamp shows in the worked span

        block.MarkRitualCompleted(At.Utc(completedAt));

        block.StartedLate.Should().BeTrue();
        block.WorkSlot().Should().Be(At.Slot(completedAt, End));
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
    public void FocusBlock_RitualDoneButNoSessionEndYet_IsNotFinal()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));

        block.IsFinal(At.Utc(End.AddHours(1))).Should().BeFalse();
    }

    [Fact]
    public void FocusBlock_MarkSessionEnded_SetsTimestamp()
    {
        var block = CreateFocusBlock();
        var endedAt = Start.AddMinutes(20);

        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(endedAt));

        block.EndedEarly.Should().BeTrue();
        block.WorkSlot().Should().Be(At.Slot(Start, endedAt));
    }

    [Fact]
    public void FocusBlock_MarkSessionEnded_Twice_Throws()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        var act = () => block.MarkSessionEnded(At.Utc(Start.AddMinutes(30)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FocusBlock_MarkSessionEnded_DoesNotMutateEnd()
    {
        var block = CreateFocusBlock();

        block.MarkRitualCompleted(At.Utc(Start));
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

    [Fact]
    public void BackgroundBlock_Create_WithARedemptionMembership_Throws_OnlyFocusTimeRedeems()
    {
        var contractId = Guid.NewGuid();

        var redemption = () => BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End),
            membership: TestContracts.Redemption(contractId));

        redemption.Should().Throw<ArgumentException>();
        BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(contractId))
            .Membership.Should().Be(TestContracts.Committed(contractId));
        BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Bonus(contractId))
            .Membership.Should().Be(TestContracts.Bonus(contractId));
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
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        block.IsActive(At.Utc(Start.AddMinutes(21))).Should().BeFalse();
    }

    [Fact]
    public void IsActive_SessionEndedEarly_BeforeActualEnd_IsStillTrue()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));
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

    [Theory]
    [InlineData(-10, false)]
    [InlineData(30, false)]
    [InlineData(60, true)] // at its end
    public void IsFinal_BackgroundBlock_OnceItHasEnded(int minutesAfterStart, bool expected)
    {
        // No ritual or session to wait for — a Background block is done when its slot is (Gap 95).
        CreateBackgroundBlock().IsFinal(At.Utc(Start.AddMinutes(minutesAfterStart))).Should().Be(expected);
    }

    [Fact]
    public void AtTheExactEnd_IsActiveHasEndedAndMissedAgree()
    {
        // Gap 88 parity: these three used to disagree at the end instant (IsActive: not active,
        // HasEnded: not ended, Classify: missed). One boundary rule — the slot's — now answers all three.
        var block = CreateFocusBlock();

        block.IsActive(At.Utc(End)).Should().BeFalse();
        block.HasEnded(At.Utc(End)).Should().BeTrue();
        block.Outcome(At.Utc(End)).Should().Be(FocusBlockOutcome.Missed);
        block.IsUnresolved(At.Utc(End)).Should().BeTrue();
    }

    [Fact]
    public void HasEnded_AfterEnd_IsTrue()
        => CreateFocusBlock().HasEnded(At.Utc(End.AddMinutes(1))).Should().BeTrue();

    [Fact]
    public void GetDuration_ReturnsEndMinusStart()
        => CreateFocusBlock().GetDuration().Should().Be(TimeSpan.FromHours(1));

    // Block 10:00–11:00; warning moment = Start − 5 min = 09:55.

    private static IEnumerable<BlockMarkerKind> Kinds(Block block, DateTime from, DateTime to)
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
    public void MarkCancelled_AfterTheBlockEnded_Throws()
    {
        // A block whose window has closed is history — it can no longer be cancelled (Gap 114, D5).
        var block = CreateBackgroundBlock();

        var act = () => block.MarkCancelled(At.Utc(End.AddHours(1)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EffectiveEnd_FocusBlock_SessionEndedEarly_ShrinksToThatEnd()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));

        block.EffectiveEnd(At.Utc(End)).Should().Be(At.Utc(Start.AddMinutes(20)));
    }

    [Fact]
    public void EffectiveEnd_FocusBlock_CancelledMidRun_ShrinksToTheCancelMoment()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkCancelled(At.Utc(Start.AddMinutes(20)));

        block.EffectiveEnd(At.Utc(End)).Should().Be(At.Utc(Start.AddMinutes(20)));
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
    public void FocusBlock_FitsMaxLength_UpToAndIncludingTheCap()
    {
        // The one place the cap is asked — the entity and both block validators (Gap 89 Slice B, A2).
        FocusBlock.FitsMaxLength(At.Slot(Start, Start.Add(FocusBlock.MaxLength))).Should().BeTrue();
        FocusBlock.FitsMaxLength(At.Slot(Start, Start.Add(FocusBlock.MaxLength).AddMinutes(1))).Should().BeFalse();
    }

    [Fact]
    public void FocusBlock_MaxLength_IsTwelveHours()
    {
        FocusBlock.MaxLength.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void FocusBlock_Create_ExceedsMaxDuration_Throws()
    {
        var act = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.Add(FocusBlock.MaxLength).AddMinutes(1)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FocusBlock_Create_AtExactlyMaxDuration_Succeeds()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.Add(FocusBlock.MaxLength)));

        block.GetDuration().Should().Be(FocusBlock.MaxLength);
    }

    [Fact]
    public void BackgroundBlock_Create_ExceedsFocusMaxDuration_Succeeds()
    {
        // Background blocks have no duration cap at all — Focus-only by design.
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.Add(FocusBlock.MaxLength).AddDays(3)));

        block.GetDuration().Should().Be(FocusBlock.MaxLength.Add(TimeSpan.FromDays(3)));
    }

    [Fact]
    public void FocusBlock_Reschedule_ExceedsMaxDuration_ThrowsAndLeavesWindowUnchanged()
    {
        var block = CreateFocusBlock();

        var act = () => block.Reschedule(At.Slot(Start, Start.Add(FocusBlock.MaxLength).AddMinutes(1)));

        act.Should().Throw<ArgumentException>();
        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public void FocusBlock_Reschedule_AtExactlyMaxDuration_Succeeds()
    {
        var block = CreateFocusBlock();
        var newEnd = Start.Add(FocusBlock.MaxLength);

        block.Reschedule(At.Slot(Start, newEnd));

        block.Scheduled.Should().Be(At.Slot(Start, newEnd));
    }

    [Fact]
    public void BackgroundBlock_Reschedule_ExceedsFocusMaxDuration_Succeeds()
    {
        var block = CreateBackgroundBlock();
        var newEnd = Start.Add(FocusBlock.MaxLength).AddDays(3);

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

    [Fact]
    public void IsRedemption_IsTheRole_NotWhetherTheBlockCoversAnything()
    {
        var contractId = Guid.NewGuid();
        var redemption = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Redemption(contractId));

        redemption.Receipt.Entries.Should().BeEmpty();
        redemption.IsRedemption.Should().BeTrue();
        redemption.CountsTowardFloor.Should().BeTrue();
        redemption.IsBonus.Should().BeFalse();
    }

    [Fact]
    public void RedemptionEntries_AreRefused_UnlessTheBlockIsARedemptionBlock()
    {
        var contractId = Guid.NewGuid();
        RedemptionEntry[] entries = [new(Guid.NewGuid(), WholeMinutes.Of(30))];
        var createPlain = () => FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), title: null, membership: null, receipt: new RedemptionReceipt(entries));
        var createCommitted = () => FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End),
            title: null, membership: TestContracts.Committed(contractId), receipt: new RedemptionReceipt(entries));
        var createBonus = () => FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End),
            title: null, membership: TestContracts.Bonus(contractId), receipt: new RedemptionReceipt(entries));

        createPlain.Should().Throw<ArgumentException>();
        createCommitted.Should().Throw<ArgumentException>();
        createBonus.Should().Throw<ArgumentException>();
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
        CreateFocusBlock().Receipt.Entries.Should().BeEmpty();
    }

    [Fact]
    public void FocusBlock_Create_WithRedemptionEntries_SetsThem()
    {
        var entries = new[] { new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(30)) };

        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), title: null, membership: TestContracts.Redemption(Guid.NewGuid()), receipt: new RedemptionReceipt(entries));

        block.Receipt.Entries.Should().BeEquivalentTo(entries);
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
        var cancelledAt = Start.AddMinutes(30);

        block.MarkCancelled(At.Utc(cancelledAt));

        block.CancelledAt.Should().Be(At.Utc(cancelledAt));
    }

    [Fact]
    public void FocusBlock_MarkCancelled_Twice_Throws()
    {
        var block = CreateFocusBlock();
        block.MarkCancelled(At.Utc(Start.AddMinutes(30)));

        var act = () => block.MarkCancelled(At.Utc(Start.AddMinutes(40)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FocusBlock_MarkCancelled_RaisesBlockCancelledEvent()
    {
        var block = CreateFocusBlock();
        var cancelledAt = Start.AddMinutes(30);

        block.MarkCancelled(At.Utc(cancelledAt));

        block.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BlockCancelledEvent>()
            .Which.Should().Match<BlockCancelledEvent>(e =>
                e.BlockId == block.Id && e.OccurredAt == At.Utc(cancelledAt));
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
        var cancelledAt = Start.AddMinutes(30);

        block.MarkCancelled(At.Utc(cancelledAt));

        block.CancelledAt.Should().Be(At.Utc(cancelledAt));
    }

    [Fact]
    public void BackgroundBlock_MarkCancelled_Twice_Throws()
    {
        var block = CreateBackgroundBlock();
        block.MarkCancelled(At.Utc(Start.AddMinutes(30)));

        var act = () => block.MarkCancelled(At.Utc(Start.AddMinutes(40)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BackgroundBlock_MarkCancelled_RaisesBlockCancelledEvent()
    {
        var block = CreateBackgroundBlock();
        var cancelledAt = Start.AddMinutes(30);

        block.MarkCancelled(At.Utc(cancelledAt));

        block.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BlockCancelledEvent>();
    }

    // ── A planned block is whole minutes long (Gap 92, V10) ──

    private static readonly DateTime OddEnd = Start.AddMinutes(30).AddSeconds(30);

    [Fact]
    public void Create_AFocusBlockNotWholeMinutesLong_Throws()
    {
        var act = () => CreateFocusBlock(Start, OddEnd);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ABackgroundBlockNotWholeMinutesLong_Throws()
    {
        var act = () => CreateBackgroundBlock(Start, OddEnd);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_AWholeMinuteLengthStartingMidMinute_IsFine()
        // The length is what counts, not where on the clock it starts.
        => CreateFocusBlock(Start.AddSeconds(20), Start.AddSeconds(20).AddMinutes(45)).Length.Should().Be(WholeMinutes.Of(45));

    [Fact]
    public void Reschedule_ToALengthNotWholeMinutes_Throws()
    {
        var act = () => CreateBackgroundBlock().Reschedule(At.Slot(Start, OddEnd));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SplitInto_PiecesNotWholeMinutesLong_Throws_EvenWhenTheyAddUp()
    {
        var middle = Start.AddMinutes(30).AddSeconds(30);

        var act = () => CreateFocusBlock().SplitInto(Guid.NewGuid(), At.Slot(Start, middle), Guid.NewGuid(), At.Slot(middle, End));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Length_IsTheSlotInWholeMinutes()
        => CreateFocusBlock().Length.Should().Be(WholeMinutes.Of(60));
}
