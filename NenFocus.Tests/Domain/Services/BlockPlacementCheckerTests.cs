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

    private PlacementVerdict CheckFree(TimeSlot slot, PlannedBlock[]? blocks = null, Session[]? sessions = null) =>
        _checker.CheckFree(slot, blocks ?? [], sessions ?? [], Now);

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
        var verdict = CheckFree(Slot(8.5, 9.5), sessions: [RunningSession(8, 9)]);

        verdict.Outcome.Should().Be(PlacementOutcome.TimeTaken);
        verdict.Conflict.Should().Be(Slot(8, 9));
    }

    [Fact]
    public void FinishedSession_IsNotInTheWay()
    {
        var session = RunningSession(7, 9);
        session.Complete(At.Utc(Day.AddHours(7.5)));

        CheckFree(Slot(8, 9), sessions: [session]).Should().Be(PlacementVerdict.Allowed);
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

        _checker.CheckFree(Slot(6, 7), [block], [], At.Utc(Day.AddHours(7.5))).Should().Be(PlacementVerdict.Allowed);
    }

    [Fact]
    public void PendingBlockAwaitingItsRitual_IsStillInTheWay()
    {
        var block = Focus(6, 7);

        _checker.CheckFree(Slot(6, 7), [block], [], At.Utc(Day.AddHours(6.2))).Conflict.Should().Be(Slot(6, 7));
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
