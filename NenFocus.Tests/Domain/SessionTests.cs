using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

/// <summary>
/// Unit tests for the Session entity state machine.
/// Zero external dependencies — no mocks required.
/// These are the canonical TDD "beachhead" tests; every new Session feature gets a test here first.
/// </summary>
public class SessionTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Session CreateActiveSession(BreakSettings? breakSettings = null)
    {
        var session = Session.Create(
            new SessionGoal("Test focus"),
            new SessionDuration(TimeSpan.FromMinutes(25)),
            Guid.NewGuid(),
            breakSettings);
        session.Start();
        return session;
    }

    private static BreakSettings DefaultBreakSettings() => new(intervalMinutes: 25, breakLengthMinutes: 5);

    // ── Start ────────────────────────────────────────────────────────────────

    [Fact]
    public void Start_TransitionsStatus_ToActive()
    {
        var session = Session.Create(
            new SessionGoal("Deep work"),
            new SessionDuration(TimeSpan.FromMinutes(45)),
            Guid.NewGuid());

        session.Start();

        session.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public void Start_SetsStartedAt_ToUtcNow()
    {
        var before = DateTime.UtcNow;
        var session = Session.Create(
            new SessionGoal("Deep work"),
            new SessionDuration(TimeSpan.FromMinutes(25)),
            Guid.NewGuid());

        session.Start();

        session.StartedAt.Should().NotBeNull();
        session.StartedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Start_WhenAlreadyStarted_Throws()
    {
        var session = CreateActiveSession();

        var act = () => session.Start();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── StartBreak ───────────────────────────────────────────────────────────

    [Fact]
    public void StartBreak_TransitionsStatus_ToOnBreak()
    {
        var session = CreateActiveSession(DefaultBreakSettings());

        session.StartBreak();

        session.Status.Should().Be(SessionStatus.OnBreak);
    }

    [Fact]
    public void StartBreak_IncrementsBreaksTaken()
    {
        var session = CreateActiveSession(DefaultBreakSettings());

        session.StartBreak();

        session.BreaksTaken.Should().Be(1);
    }

    [Fact]
    public void StartBreak_SetsBreakStartedAt()
    {
        var before = DateTime.UtcNow;
        var session = CreateActiveSession(DefaultBreakSettings());

        session.StartBreak();

        session.BreakStartedAt.Should().NotBeNull();
        session.BreakStartedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void StartBreak_WithoutBreakSettings_Throws()
    {
        var session = CreateActiveSession(breakSettings: null);

        var act = () => session.StartBreak();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StartBreak_WhenNotActive_Throws()
    {
        var session = Session.Create(
            new SessionGoal("Test"),
            new SessionDuration(TimeSpan.FromMinutes(25)),
            Guid.NewGuid(),
            DefaultBreakSettings());

        var act = () => session.StartBreak();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── ResumeFromBreak ──────────────────────────────────────────────────────

    [Fact]
    public void ResumeFromBreak_TransitionsStatus_BackToActive()
    {
        var session = CreateActiveSession(DefaultBreakSettings());
        session.StartBreak();

        session.ResumeFromBreak();

        session.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public void ResumeFromBreak_ClearsBreakStartedAt()
    {
        var session = CreateActiveSession(DefaultBreakSettings());
        session.StartBreak();

        session.ResumeFromBreak();

        session.BreakStartedAt.Should().BeNull();
    }

    [Fact]
    public void ResumeFromBreak_WhenNotOnBreak_Throws()
    {
        var session = CreateActiveSession(DefaultBreakSettings());

        var act = () => session.ResumeFromBreak();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Complete ─────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_TransitionsStatus_ToCompleted()
    {
        var session = CreateActiveSession();

        session.Complete();

        session.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public void Complete_SetsEndedAt()
    {
        var before = DateTime.UtcNow;
        var session = CreateActiveSession();

        session.Complete();

        session.EndedAt.Should().NotBeNull();
        session.EndedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_Throws()
    {
        var session = CreateActiveSession();
        session.Complete();

        var act = () => session.Complete();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Full cycle ───────────────────────────────────────────────────────────

    [Fact]
    public void FullCycle_TwoBreaks_TracksBreakCount()
    {
        var session = CreateActiveSession(DefaultBreakSettings());

        session.StartBreak();
        session.ResumeFromBreak();
        session.StartBreak();
        session.ResumeFromBreak();
        session.Complete();

        session.BreaksTaken.Should().Be(2);
        session.Status.Should().Be(SessionStatus.Completed);
    }
}
