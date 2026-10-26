using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

/// <summary>
/// Where a running session is at a moment (Gap 92, A4): one answer the Main page, enforcement and
/// the session itself all read, so the screen and the blocking can't disagree. Times are minutes
/// after a 09:00 start; the run is 120 min with a 5 min break at 25–30 and a 15 min long break at 55–70.
/// </summary>
public class SessionTimelineTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    private static readonly BreakSchedule TwoBreaks = new([
        new SessionBreak(BreakKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)),
        new SessionBreak(BreakKind.LongBreak, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(70))]);

    private static SessionTimeline Timeline(bool keepsBlocking = false) =>
        new(At.Slot(Start, Start.AddMinutes(120)), TwoBreaks, keepsBlocking);

    private static Moment Minute(double minutes) => At.Utc(Start.AddMinutes(minutes));

    [Fact]
    public void WhileWorking_ItIsTheRunAndNoBreak()
    {
        var reading = Timeline().ReadingAt(Minute(10));

        reading.TimeRemaining.Should().Be(TimeSpan.FromMinutes(110));
        reading.Progress.Should().BeApproximately(10 / 120.0, 1e-9);
        reading.IsOnBreak.Should().BeFalse();
        reading.CurrentBreak.Should().BeNull();
        reading.BreakTimeRemaining.Should().Be(TimeSpan.Zero);
        reading.BreaksTaken.Should().Be(0);
        reading.LiftsBlocking.Should().BeFalse();
        reading.KeepsBlockingDuringBreak.Should().BeFalse("not on a break, so there is no break to keep blocking through (Gap 113, S10)");
        reading.HasEnded.Should().BeFalse();
    }

    [Fact]
    public void AtTheExactMomentABreakStarts_ItIsOnBreak()
    {
        var reading = Timeline().ReadingAt(Minute(25));

        reading.IsOnBreak.Should().BeTrue();
        reading.BreakTimeRemaining.Should().Be(TimeSpan.FromMinutes(5));
        reading.BreakProgress.Should().Be(0);
        reading.BreaksTaken.Should().Be(1);
        reading.LiftsBlocking.Should().BeTrue();
    }

    [Fact]
    public void AtTheExactMomentABreakEnds_ItIsWorkAgain()
    {
        var reading = Timeline().ReadingAt(Minute(30));

        reading.IsOnBreak.Should().BeFalse();
        reading.BreaksTaken.Should().Be(1);
    }

    [Fact]
    public void InABreak_OfAContractThatKeepsBlocking_BlockingIsNotLifted()
    {
        var reading = Timeline(keepsBlocking: true).ReadingAt(Minute(60));

        reading.IsOnBreak.Should().BeTrue();
        reading.CurrentBreak!.Kind.Should().Be(BreakKind.LongBreak);
        reading.LiftsBlocking.Should().BeFalse();
        reading.KeepsBlockingDuringBreak.Should().BeTrue();
    }

    [Fact]
    public void OnceTheRunHasEnded_NothingIsLeft_AndEveryBreakWasTaken()
    {
        var reading = Timeline().ReadingAt(Minute(120));

        reading.HasEnded.Should().BeTrue();
        reading.TimeRemaining.Should().Be(TimeSpan.Zero);
        reading.Progress.Should().Be(1);
        reading.IsOnBreak.Should().BeFalse();
        reading.BreaksTaken.Should().Be(2);
    }

    [Fact]
    public void TransitionsBetween_AreTheBoundariesCrossed()
        => Timeline().TransitionsBetween(Minute(20), Minute(56)).Should().HaveCount(3); // started, ended, started

    [Fact]
    public void Marks_PlaceEachBreakOnTheRun()
        => Timeline().Marks.Should().Equal(
            new BreakMark(25 / 120.0, BreakKind.ShortBreak),
            new BreakMark(55 / 120.0, BreakKind.LongBreak));

    [Fact]
    public void BreaksThatDontFitTheRun_AreRefused()
    {
        var act = () => new SessionTimeline(At.Slot(Start, Start.AddMinutes(60)), TwoBreaks, keepsBlockingDuringBreaks: false);

        act.Should().Throw<ArgumentException>();
    }

    // ── The session's own answers are the timeline's (parity: the screen and the blocking agree) ──

    [Fact]
    public void ASessionsBreakAnswers_AreItsTimelines_AtEveryMinute()
    {
        foreach (var keepsBlocking in new[] { false, true })
        {
            var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
                new SessionDuration(TimeSpan.FromMinutes(120)), Guid.NewGuid(), SessionOrigin.Spontaneous);
            session.Start(At.Utc(Start), breakSchedule: TwoBreaks, keepBlockingDuringBreaks: keepsBlocking);

            for (var minute = 0; minute <= 120; minute++)
            {
                var reading = session.Timeline!.ReadingAt(Minute(minute));
                session.LiftsBlockingAt(Minute(minute)).Should().Be(reading.LiftsBlocking, $"minute {minute}");
                session.IsOnBreakAt(Minute(minute)).Should().Be(reading.IsOnBreak, $"minute {minute}");
                session.BreaksTakenBy(Minute(minute)).Should().Be(reading.BreaksTaken, $"minute {minute}");
            }
        }
    }
}
