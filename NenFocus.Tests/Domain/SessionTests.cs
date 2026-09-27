using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class SessionTests
{
    private static Session CreateSpontaneousSession()
        => Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal("Write tests"),
            duration: new SessionDuration(TimeSpan.FromMinutes(30)),
            blockPresetId: Guid.NewGuid(),
            origin: SessionOrigin.Spontaneous);

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

    // ── Break schedule (Gap 63) ──────────────────────────────────────────────

    private static readonly DateTime T0 = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private static BreakSchedule OneShortBreak(int startMinute = 25, int endMinute = 30) =>
        new([new SessionBreak(SessionPhaseKind.ShortBreak,
            TimeSpan.FromMinutes(startMinute), TimeSpan.FromMinutes(endMinute))]);

    private static Session StartedTwoHourSession(BreakSchedule schedule, string? presetName = "Classic")
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Write tests"),
            new SessionDuration(TimeSpan.FromHours(2)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(T0, breakSchedule: schedule, breakPresetName: presetName);
        return session;
    }

    [Fact]
    public void Start_WithoutSchedule_HasNoBreaksAndNoLabel()
    {
        var session = CreateSpontaneousSession();

        session.Start(T0);

        session.BreakSchedule.Should().Be(BreakSchedule.None);
        session.BreakPresetName.Should().BeNull();
    }

    [Fact]
    public void Start_WithSchedule_StoresItAndThePresetLabel()
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.BreakSchedule.Should().Be(OneShortBreak());
        session.BreakPresetName.Should().Be("Classic");
    }

    [Fact]
    public void Start_EmptyScheduleWithPresetName_IsLabelledAsNoBreaks()
    {
        var session = StartedTwoHourSession(BreakSchedule.None, presetName: "Classic");

        session.BreakPresetName.Should().BeNull();
    }

    [Fact]
    public void Start_ScheduleNotFittingTheRun_Throws()
    {
        // 30-minute session; a break ending at 30 leaves no work after it.
        var session = CreateSpontaneousSession();

        var act = () => session.Start(T0, breakSchedule: OneShortBreak(25, 30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Start_PlannedSession_ChecksScheduleAgainstActualRunNotNominalDuration()
    {
        // Planned block is nominally 30 minutes, but the Ritual finished 10 minutes late:
        // only 20 minutes remain, so a 25-30 break cannot fit.
        var session = CreatePlannedSession(Guid.NewGuid());

        var act = () => session.Start(T0, T0.AddMinutes(20), OneShortBreak(25, 30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RunLength_UsesScheduledEndWhenGiven_OtherwiseDuration()
    {
        var session = CreateSpontaneousSession(); // 30 minutes

        session.RunLength(T0).Should().Be(TimeSpan.FromMinutes(30));
        session.RunLength(T0, T0.AddMinutes(12)).Should().Be(TimeSpan.FromMinutes(12));
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(25, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    public void IsOnBreakAt_ReadsTheScheduleAgainstTheClock(int minute, bool onBreak)
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.IsOnBreakAt(T0.AddMinutes(minute)).Should().Be(onBreak);
        session.Status.Should().Be(SessionStatus.Active, "a break never changes the session's status");
    }

    [Fact]
    public void IsOnBreakAt_OnceCompleted_False()
    {
        var session = StartedTwoHourSession(OneShortBreak());
        session.Complete(T0.AddMinutes(27));

        session.IsOnBreakAt(T0.AddMinutes(27)).Should().BeFalse();
    }

    [Fact]
    public void CurrentBreakAt_WakingMidBreak_ReportsTheSameSlotSoRemainingTimeIsCorrect()
    {
        // Laptop closed before 17:25, opened at 17:27 — the break still ends at 17:30.
        var session = StartedTwoHourSession(OneShortBreak());

        var current = session.CurrentBreakAt(T0.AddMinutes(27));

        current!.End.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void BreakTransitionsBetween_TranslatesClockTimesToScheduleOffsets()
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.BreakTransitionsBetween(T0.AddMinutes(24), T0.AddMinutes(26))
            .Should().ContainSingle().Which.Kind.Should().Be(BreakTransitionKind.Started);
    }

    [Fact]
    public void BreaksTakenBy_CountsBreaksBegunSoFar()
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.BreaksTakenBy(T0.AddMinutes(10)).Should().Be(0);
        session.BreaksTakenBy(T0.AddMinutes(40)).Should().Be(1);
    }

    [Fact]
    public void Complete_AfterPlannedEnd_IsCappedAtEndAt()
    {
        // Regression: a session whose end was noticed late recorded 2h31m for a 2h plan.
        var session = StartedTwoHourSession(BreakSchedule.None);

        session.Complete(T0.AddHours(2).AddMinutes(1));

        session.EndedAt.Should().Be(T0.AddHours(2));
        session.DomainEvents.OfType<SessionEndedEvent>().Single().EndedAt.Should().Be(T0.AddHours(2));
    }

    [Fact]
    public void Complete_MidBreak_TrimsThatBreakToTheActualEnd()
    {
        var session = StartedTwoHourSession(new BreakSchedule([
            new SessionBreak(SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)),
            new SessionBreak(SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(60))]));

        session.Complete(T0.AddMinutes(27));

        session.BreakSchedule.Should().Be(OneShortBreak(25, 27));
        session.BreaksTaken.Should().Be(1);
    }

    [Fact]
    public void Complete_ActiveSession_CompletesAndRaisesEvent()
    {
        var session = CreateSpontaneousSession(); // 30 minutes
        session.Start(T0);
        var endedAt = T0.AddMinutes(20);

        session.Complete(endedAt);

        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().Be(endedAt);
        session.DomainEvents.OfType<SessionEndedEvent>().Should().ContainSingle();
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
    public void ClearDomainEvents_RemovesAllRaisedEvents()
    {
        var session = CreateSpontaneousSession();
        session.Start(DateTime.UtcNow);

        session.ClearDomainEvents();

        session.DomainEvents.Should().BeEmpty();
    }
}
