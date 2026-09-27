using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class SessionTests
{
    private static Session CreateSpontaneousSession(BreakTimer? breakTimer = null)
        => Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal("Write tests"),
            duration: new SessionDuration(TimeSpan.FromMinutes(30)),
            blockPresetId: Guid.NewGuid(),
            origin: SessionOrigin.Spontaneous,
            breakTimer: breakTimer);

    private static Session CreatePlannedSession(Guid originatingBlockId)
        => Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal("Write tests"),
            duration: new SessionDuration(TimeSpan.FromMinutes(30)),
            blockPresetId: Guid.NewGuid(),
            origin: SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);

    [Fact]
    public void Create_Spontaneous_HasNoOriginatingBlock()
    {
        var session = CreateSpontaneousSession();

        session.Origin.Should().Be(SessionOrigin.Spontaneous);
        session.OriginatingBlockId.Should().BeNull();
        session.Status.Should().Be(SessionStatus.NotStarted);
        session.TemporaryBlockedApps.Should().BeEmpty();
        session.TemporaryBlockedSites.Should().BeEmpty();
    }

    [Fact]
    public void Create_Planned_WithoutOriginatingBlockId_Throws()
    {
        var act = () => Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Goal"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            SessionOrigin.Planned);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Spontaneous_WithOriginatingBlockId_Throws()
    {
        var act = () => Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Goal"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            SessionOrigin.Spontaneous,
            originatingBlockId: Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Planned_WithOriginatingBlockId_Succeeds()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);

        session.Origin.Should().Be(SessionOrigin.Planned);
        session.OriginatingBlockId.Should().Be(blockId);
    }

    [Fact]
    public void Start_FromNotStarted_ActivatesAndRaisesEvent()
    {
        var session = CreateSpontaneousSession();
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        session.Start(startedAt);

        session.Status.Should().Be(SessionStatus.Active);
        session.StartedAt.Should().Be(startedAt);
        session.EndAt.Should().Be(startedAt + session.Duration.Value);
        session.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SessionStartedEvent>();

        var raised = (SessionStartedEvent)session.DomainEvents.Single();
        raised.SessionId.Should().Be(session.Id);
        raised.BlockPresetId.Should().Be(session.BlockPresetId);
        raised.Goal.Should().Be(session.Goal.Value);
    }

    [Fact]
    public void Start_WithScheduledEnd_OverridesDurationDerivedEndAt()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId); // 30 minute duration
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = startedAt.AddMinutes(3);

        session.Start(startedAt, scheduledEnd);

        session.EndAt.Should().Be(scheduledEnd);
    }

    [Fact]
    public void Start_AlreadyStarted_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        var act = () => session.Start(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StartBreak_ActiveWithBreakTimer_MovesToOnBreakAndRaisesEvent()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));
        session.Start(DateTime.UtcNow);
        var breakStartedAt = DateTime.UtcNow;

        session.StartBreak(breakStartedAt, SessionPhaseKind.ShortBreak);

        session.Status.Should().Be(SessionStatus.OnBreak);
        session.BreakStartedAt.Should().Be(breakStartedAt);
        session.CurrentBreakKind.Should().Be(SessionPhaseKind.ShortBreak);
        session.BreaksTaken.Should().Be(1);

        var raised = session.DomainEvents.OfType<BreakStartedEvent>().Single();
        raised.BreakNumber.Should().Be(1);
    }

    [Fact]
    public void StartBreak_WithoutBreakTimer_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        var act = () => session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StartBreak_WithWorkAsBreakKind_Throws()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));
        session.Start(DateTime.UtcNow);

        var act = () => session.StartBreak(DateTime.UtcNow, SessionPhaseKind.Work);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StartBreak_WhenNotActive_Throws()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));

        var act = () => session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StartBreak_Twice_IncrementsBreaksTaken()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));
        session.Start(DateTime.UtcNow);

        session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);
        session.ResumeFromBreak(DateTime.UtcNow);
        session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);

        session.BreaksTaken.Should().Be(2);
        session.DomainEvents.OfType<BreakStartedEvent>().Select(e => e.BreakNumber)
            .Should().Equal(1, 2);
    }

    [Fact]
    public void ResumeFromBreak_WhenOnBreak_ReturnsToActive()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));
        session.Start(DateTime.UtcNow);
        session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);

        session.ResumeFromBreak(DateTime.UtcNow);

        session.Status.Should().Be(SessionStatus.Active);
        session.BreakStartedAt.Should().BeNull();
    }

    [Fact]
    public void ResumeFromBreak_WhenOnBreak_RaisesBreakEndedEvent()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));
        session.Start(DateTime.UtcNow);
        session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);
        var resumedAt = DateTime.UtcNow.AddMinutes(5);

        session.ResumeFromBreak(resumedAt);

        var raised = session.DomainEvents.OfType<BreakEndedEvent>().Single();
        raised.SessionId.Should().Be(session.Id);
        raised.BreakNumber.Should().Be(1);
        raised.OccurredAt.Should().Be(resumedAt);
    }

    [Fact]
    public void ResumeFromBreak_WhenNotOnBreak_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        var act = () => session.ResumeFromBreak(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_ActiveSession_CompletesAndRaisesEvent()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);
        var endedAt = DateTime.UtcNow.AddMinutes(30);

        session.Complete(endedAt);

        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().Be(endedAt);
        session.DomainEvents.OfType<SessionEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Complete_WhileOnBreak_ClearsBreakStartedAt()
    {
        var session = CreateSpontaneousSession(new BreakTimer(25, 5, 4, 15));
        session.Start(DateTime.UtcNow);
        session.StartBreak(DateTime.UtcNow, SessionPhaseKind.ShortBreak);

        session.Complete(DateTime.UtcNow);

        session.BreakStartedAt.Should().BeNull();
        session.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public void Complete_AlreadyCompleted_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);
        session.Complete(DateTime.UtcNow);

        var act = () => session.Complete(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetReflection_OnCompletedSession_TrimsAndStores()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);
        session.Complete(DateTime.UtcNow);

        session.SetReflection("  Went well  ");

        session.Reflection.Should().Be("Went well");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetReflection_BlankValue_ClearsReflection(string? blank)
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);
        session.Complete(DateTime.UtcNow);

        session.SetReflection(blank);

        session.Reflection.Should().BeNull();
    }

    [Fact]
    public void SetReflection_NotCompleted_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        var act = () => session.SetReflection("Too soon");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetReflection_OnMissedSession_Succeeds()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        session.Miss(scheduledStart, scheduledStart.AddHours(1));

        session.SetReflection("Got distracted");

        session.Reflection.Should().Be("Got distracted");
    }

    [Fact]
    public void MarkPostSessionAcknowledged_OnCompletedSession_StampsTimestamp()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);
        session.Complete(DateTime.UtcNow);
        var acknowledgedAt = DateTime.UtcNow;

        session.MarkPostSessionAcknowledged(acknowledgedAt);

        session.PostSessionAcknowledgedAt.Should().Be(acknowledgedAt);
    }

    [Fact]
    public void MarkPostSessionAcknowledged_OnMissedSession_Succeeds()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        session.Miss(scheduledStart, scheduledStart.AddHours(1));
        var acknowledgedAt = DateTime.UtcNow;

        session.MarkPostSessionAcknowledged(acknowledgedAt);

        session.PostSessionAcknowledgedAt.Should().Be(acknowledgedAt);
    }

    [Fact]
    public void MarkPostSessionAcknowledged_NotCompletedOrMissed_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        var act = () => session.MarkPostSessionAcknowledged(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Miss_FromNotStarted_TransitionsAndRaisesEvent()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = scheduledStart.AddHours(1);

        session.Miss(scheduledStart, scheduledEnd);

        session.Status.Should().Be(SessionStatus.Missed);
        session.StartedAt.Should().Be(scheduledStart);
        session.EndedAt.Should().Be(scheduledEnd);
        var raised = session.DomainEvents.OfType<SessionMissedEvent>().Single();
        raised.SessionId.Should().Be(session.Id);
        raised.ScheduledStart.Should().Be(scheduledStart);
        raised.ScheduledEnd.Should().Be(scheduledEnd);
    }

    [Fact]
    public void Miss_WhenOriginSpontaneous_Throws()
    {
        var session = CreateSpontaneousSession();

        var act = () => session.Miss(DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Miss_WhenAlreadyActive_Throws()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        session.Start(DateTime.UtcNow);

        var act = () => session.Miss(DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Miss_WhenAlreadyMissed_Throws()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        var scheduledStart = DateTime.UtcNow;
        session.Miss(scheduledStart, scheduledStart.AddHours(1));

        var act = () => session.Miss(scheduledStart, scheduledStart.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetElapsedTime_WhenMissed_ReturnsZero_RegardlessOfStartEnd()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        session.Miss(scheduledStart, scheduledStart.AddHours(2));

        session.GetElapsedTime(scheduledStart.AddHours(10)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetElapsedTime_WhenCompleted_IsUnaffectedByMissedChange()
    {
        // Regression pin: the Missed short-circuit in GetElapsedTime must not
        // touch the Completed path's existing EndedAt-based calculation.
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession();
        session.Start(startedAt);
        session.Complete(startedAt.AddMinutes(15));

        var elapsed = session.GetElapsedTime(startedAt.AddHours(5));

        elapsed.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void GetElapsedTime_NotStarted_ReturnsNull()
    {
        var session = CreateSpontaneousSession();

        session.GetElapsedTime(DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void GetElapsedTime_Active_MeasuresAgainstAsOf()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession();
        session.Start(startedAt);

        var elapsed = session.GetElapsedTime(startedAt.AddMinutes(10));

        elapsed.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void GetElapsedTime_Completed_IgnoresAsOfAndUsesEndedAt()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession();
        session.Start(startedAt);
        session.Complete(startedAt.AddMinutes(15));

        var elapsed = session.GetElapsedTime(startedAt.AddHours(5));

        elapsed.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void GetRemainingTime_NotStarted_ReturnsNull()
    {
        var session = CreateSpontaneousSession();

        session.GetRemainingTime(DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void GetRemainingTime_BeforeDurationElapsed_ReturnsDifference()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession(); // 30 minute duration
        session.Start(startedAt);

        var remaining = session.GetRemainingTime(startedAt.AddMinutes(10));

        remaining.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void GetRemainingTime_PastDuration_ClampsToZero()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession(); // 30 minute duration
        session.Start(startedAt);

        var remaining = session.GetRemainingTime(startedAt.AddHours(2));

        remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetRemainingTime_WithScheduledEndOverride_IgnoresDuration()
    {
        // Regression pin: a Planned session's remaining time must track the
        // originating block's real end, not the (possibly stale) Duration
        // captured when the Ritual wizard was entered.
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId); // 30 minute duration
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        session.Start(startedAt, startedAt.AddMinutes(3));

        var remaining = session.GetRemainingTime(startedAt.AddMinutes(1));

        remaining.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SyncState_OverwritesBreakTrackingFields()
    {
        var session = CreateSpontaneousSession();
        var breakStartedAt = DateTime.UtcNow;

        session.SyncState(breaksTaken: 3, breakStartedAt: breakStartedAt);

        session.BreaksTaken.Should().Be(3);
        session.BreakStartedAt.Should().Be(breakStartedAt);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllRaisedEvents()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        session.ClearDomainEvents();

        session.DomainEvents.Should().BeEmpty();
    }
}
