using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class BreakScheduleTests
{
    private static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);

    private static SessionBreak Short(int startMinute, int endMinute) =>
        new(BreakKind.ShortBreak, M(startMinute), M(endMinute));

    private static SessionBreak Long(int startMinute, int endMinute) =>
        new(BreakKind.LongBreak, M(startMinute), M(endMinute));

    // ── SessionBreak invariants ──────────────────────────────────────────────

    [Fact]
    public void SessionBreak_EndNotAfterStart_Throws()
    {
        var act = () => new SessionBreak(BreakKind.ShortBreak, M(30), M(30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SessionBreak_StartingAtSessionStart_Throws()
    {
        var act = () => new SessionBreak(BreakKind.ShortBreak, TimeSpan.Zero, M(5));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SessionBreak_RemainingAt_IsTheTimeLeftInTheBreak()
        => Short(25, 30).RemainingAt(M(27)).Should().Be(M(3));

    [Fact]
    public void SessionBreak_ProgressAt_IsTheFractionPassed_HeldBetweenZeroAndOne()
    {
        Short(25, 30).ProgressAt(M(26)).Should().BeApproximately(0.2, 1e-9);
        Short(25, 30).ProgressAt(M(20)).Should().Be(0.0);
        Short(25, 30).ProgressAt(M(40)).Should().Be(1.0);
    }

    // ── BreakSchedule invariants ─────────────────────────────────────────────

    [Fact]
    public void Constructor_OrdersBreaksByStart()
    {
        var schedule = new BreakSchedule([Short(55, 60), Short(25, 30)]);

        schedule.Breaks.Should().Equal(Short(25, 30), Short(55, 60));
    }

    [Fact]
    public void Constructor_OverlappingBreaks_Throws()
    {
        var act = () => new BreakSchedule([Short(25, 35), Short(30, 40)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_BackToBackBreaks_Throws()
    {
        // A break must be followed by work before the next one — two touching breaks
        // are really one break wearing two labels.
        var act = () => new BreakSchedule([Short(25, 30), Short(30, 35)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void None_HasNoBreaks()
    {
        BreakSchedule.None.Breaks.Should().BeEmpty();
        BreakSchedule.None.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        new BreakSchedule([Short(25, 30)]).Should().Be(new BreakSchedule([Short(25, 30)]));
    }

    // ── FitsWithin ───────────────────────────────────────────────────────────

    [Fact]
    public void FitsWithin_LastBreakEndsBeforeLength_True()
    {
        new BreakSchedule([Short(25, 30)]).FitsWithin(M(31)).Should().BeTrue();
    }

    [Fact]
    public void FitsWithin_LastBreakEndsExactlyAtLength_False()
    {
        new BreakSchedule([Short(25, 30)]).FitsWithin(M(30)).Should().BeFalse();
    }

    [Fact]
    public void FitsWithin_Empty_AlwaysTrue()
    {
        BreakSchedule.None.FitsWithin(M(1)).Should().BeTrue();
    }

    // ── BreakAt ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(24.99, false)]
    [InlineData(25, true)]      // a break starts on its first instant
    [InlineData(29.99, true)]
    [InlineData(30, false)]     // and is over on its last
    public void BreakAt_IsHalfOpen(double minute, bool onBreak)
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        (schedule.BreakAt(M(minute)) is not null).Should().Be(onBreak);
    }

    [Fact]
    public void BreakAt_ReturnsTheBreakCoveringTheOffset()
    {
        var schedule = new BreakSchedule([Short(25, 30), Long(115, 130)]);

        schedule.BreakAt(M(120)).Should().Be(Long(115, 130));
    }

    [Fact]
    public void BreaksStartedBy_CountsBreaksWhoseStartHasBeenReached()
    {
        var schedule = new BreakSchedule([Short(25, 30), Short(55, 60)]);

        schedule.BreaksStartedBy(M(24)).Should().Be(0);
        schedule.BreaksStartedBy(M(25)).Should().Be(1);
        schedule.BreaksStartedBy(M(70)).Should().Be(2);
    }

    // ── TransitionsBetween ───────────────────────────────────────────────────

    [Fact]
    public void TransitionsBetween_NoBoundaryCrossed_Empty()
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        schedule.TransitionsBetween(M(10), M(11)).Should().BeEmpty();
    }

    [Fact]
    public void TransitionsBetween_CrossingBreakStart_ReportsStarted()
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        schedule.TransitionsBetween(M(24.99), M(25.01)).Should().Equal(
            new BreakTransition(BreakTransitionKind.Started, BreakKind.ShortBreak, M(25)));
    }

    [Fact]
    public void TransitionsBetween_IsHalfOpen_BoundaryOnToCounts_BoundaryOnFromDoesNot()
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        schedule.TransitionsBetween(M(24), M(25)).Should().HaveCount(1);
        schedule.TransitionsBetween(M(25), M(26)).Should().BeEmpty();
    }

    [Fact]
    public void TransitionsBetween_TickLandingExactlyOnABreakEnd_ReportsEndedOnce()
    {
        // Two ticks either side of the boundary: BreakEnded must fire on the first, not again on the second (Gap 113, S6).
        var schedule = new BreakSchedule([Short(25, 30)]);
        var ended = new BreakTransition(BreakTransitionKind.Ended, BreakKind.ShortBreak, M(30));

        schedule.TransitionsBetween(M(29), M(30)).Should().Equal(ended);
        schedule.TransitionsBetween(M(30), M(31)).Should().BeEmpty();
    }

    [Fact]
    public void TransitionsBetween_WholeBreakSkipped_ReportsBothInOrder()
    {
        var schedule = new BreakSchedule([Long(115, 130)]);

        schedule.TransitionsBetween(M(100), M(140)).Should().Equal(
            new BreakTransition(BreakTransitionKind.Started, BreakKind.LongBreak, M(115)),
            new BreakTransition(BreakTransitionKind.Ended, BreakKind.LongBreak, M(130)));
    }

    [Fact]
    public void TransitionsBetween_BackwardsWindow_Empty()
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        schedule.TransitionsBetween(M(40), M(20)).Should().BeEmpty();
    }

    // ── TrimTo ───────────────────────────────────────────────────────────────

    [Fact]
    public void TrimTo_DropsBreaksNotYetStarted_AndCutsTheOneInProgress()
    {
        var schedule = new BreakSchedule([Short(25, 30), Short(55, 60), Short(85, 90)]);

        schedule.TrimTo(M(57)).Breaks.Should().Equal(Short(25, 30), Short(55, 57));
    }

    [Fact]
    public void TrimTo_ExactlyAtABreakStart_DropsThatBreak()
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        schedule.TrimTo(M(25)).Breaks.Should().BeEmpty();
    }

    [Fact]
    public void TrimTo_AfterEveryBreak_KeepsAll()
    {
        var schedule = new BreakSchedule([Short(25, 30)]);

        schedule.TrimTo(M(90)).Should().Be(schedule);
    }
}
