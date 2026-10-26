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

        session.Start(At.Utc(startedAt));

        session.Status.Should().Be(SessionStatus.Active);
        session.StartedAt.Should().Be(At.Utc(startedAt));
        session.EndAt.Should().Be(At.Utc(startedAt + session.Duration.Value));
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

        session.Start(At.Utc(startedAt), At.PlannedWindowEnding(scheduledEnd));

        session.EndAt.Should().Be(At.Utc(scheduledEnd));
    }

    [Fact]
    public void Start_AlreadyStarted_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));

        var act = () => session.Start(At.Utc(DateTime.UtcNow));

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Break schedule (Gap 63) ──────────────────────────────────────────────

    private static readonly DateTime T0 = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private static BreakSchedule OneShortBreak(int startMinute = 25, int endMinute = 30) =>
        new([new SessionBreak(BreakKind.ShortBreak,
            TimeSpan.FromMinutes(startMinute), TimeSpan.FromMinutes(endMinute))]);

    private static Session StartedTwoHourSession(BreakSchedule schedule, string? presetName = "Classic")
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Write tests"),
            new SessionDuration(TimeSpan.FromHours(2)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(T0), breakSchedule: schedule, breakPresetName: presetName);
        return session;
    }

    [Fact]
    public void Start_WithoutSchedule_HasNoBreaksAndNoLabel()
    {
        var session = CreateSpontaneousSession();

        session.Start(At.Utc(T0));

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

        var act = () => session.Start(At.Utc(T0), breakSchedule: OneShortBreak(25, 30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Start_PlannedSession_ChecksScheduleAgainstActualRunNotNominalDuration()
    {
        // Planned block is nominally 30 minutes, but the Ritual finished 10 minutes late:
        // only 20 minutes remain, so a 25-30 break cannot fit.
        var session = CreatePlannedSession(Guid.NewGuid());

        var act = () => session.Start(At.Utc(T0), At.PlannedWindowEnding(T0.AddMinutes(20)), OneShortBreak(25, 30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RunLength_UsesScheduledEndWhenGiven_OtherwiseDuration()
    {
        var session = CreateSpontaneousSession(); // 30 minutes

        session.RunLength(At.Utc(T0)).Should().Be(TimeSpan.FromMinutes(30));
        session.RunLength(At.Utc(T0), At.PlannedWindowEnding(T0.AddMinutes(12))).Should().Be(TimeSpan.FromMinutes(12));
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(25, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    public void IsOnBreakAt_ReadsTheScheduleAgainstTheClock(int minute, bool onBreak)
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.IsOnBreakAt(At.Utc(T0.AddMinutes(minute))).Should().Be(onBreak);
        session.Status.Should().Be(SessionStatus.Active, "a break never changes the session's status");
    }

    [Fact]
    public void IsOnBreakAt_OnceCompleted_False()
    {
        var session = StartedTwoHourSession(OneShortBreak());
        session.Complete(At.Utc(T0.AddMinutes(27)), null);

        session.IsOnBreakAt(At.Utc(T0.AddMinutes(27))).Should().BeFalse();
    }

    [Fact]
    public void CurrentBreakAt_WakingMidBreak_ReportsTheSameSlotSoRemainingTimeIsCorrect()
    {
        // Laptop closed before 17:25, opened at 17:27 — the break still ends at 17:30.
        var session = StartedTwoHourSession(OneShortBreak());

        var current = session.CurrentBreakAt(At.Utc(T0.AddMinutes(27)));

        current!.End.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void BreakTransitionsBetween_TranslatesClockTimesToScheduleOffsets()
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.BreakTransitionsBetween(At.Utc(T0.AddMinutes(24)), At.Utc(T0.AddMinutes(26)))
            .Should().ContainSingle().Which.Kind.Should().Be(BreakTransitionKind.Started);
    }

    [Fact]
    public void BreakTransitionsBetween_NotStarted_IsEmpty()
    {
        var session = CreateSpontaneousSession();

        session.BreakTransitionsBetween(At.Utc(T0), At.Utc(T0.AddHours(1))).Should().BeEmpty();
    }

    [Fact]
    public void BreaksTakenBy_CountsBreaksBegunSoFar()
    {
        var session = StartedTwoHourSession(OneShortBreak());

        session.BreaksTakenBy(At.Utc(T0.AddMinutes(10))).Should().Be(0);
        session.BreaksTakenBy(At.Utc(T0.AddMinutes(40))).Should().Be(1);
    }

    // ── Keep blocking during breaks (Gap 64) ─────────────────────────────────

    [Fact]
    public void Start_ByDefault_DoesNotKeepBlockingDuringBreaks()
    {
        StartedTwoHourSession(OneShortBreak()).KeepBlockingDuringBreaks.Should().BeFalse();
    }

    [Theory]
    [InlineData(10, false, false)] // working — nothing to lift
    [InlineData(27, false, true)]  // in a break slot — lifts
    [InlineData(27, true, false)]  // in a break slot, but the contract keeps blocking
    public void LiftsBlockingAt_OnlyInsideABreakSlotAndOnlyWhenNotKeepingBlocking(
        int minute, bool keepBlocking, bool lifts)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Write tests"),
            new SessionDuration(TimeSpan.FromHours(2)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(T0), breakSchedule: OneShortBreak(), keepBlockingDuringBreaks: keepBlocking);

        session.LiftsBlockingAt(At.Utc(T0.AddMinutes(minute))).Should().Be(lifts);
        session.IsOnBreakAt(At.Utc(T0.AddMinutes(minute))).Should().Be(minute == 27,
            "keeping blocking changes what a break does, not whether it is a break");
    }

    [Fact]
    public void LiftsBlockingAt_OnceCompleted_False()
    {
        var session = StartedTwoHourSession(OneShortBreak());
        session.Complete(At.Utc(T0.AddMinutes(27)), null);

        session.LiftsBlockingAt(At.Utc(T0.AddMinutes(27))).Should().BeFalse();
    }

    [Fact]
    public void Complete_AfterPlannedEnd_IsCappedAtEndAt()
    {
        // Regression: a session whose end was noticed late recorded 2h31m for a 2h plan.
        var session = StartedTwoHourSession(BreakSchedule.None);

        session.Complete(At.Utc(T0.AddHours(2).AddMinutes(1)), null);

        session.EndedAt.Should().Be(At.Utc(T0.AddHours(2)));
        session.DomainEvents.OfType<SessionEndedEvent>().Single().EndedAt.Should().Be(At.Utc(T0.AddHours(2)));
    }

    [Fact]
    public void Complete_MidBreak_TrimsThatBreakToTheActualEnd()
    {
        var session = StartedTwoHourSession(new BreakSchedule([
            new SessionBreak(BreakKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)),
            new SessionBreak(BreakKind.ShortBreak, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(60))]));

        session.Complete(At.Utc(T0.AddMinutes(27)), null);

        session.BreakSchedule.Should().Be(OneShortBreak(25, 27));
        session.BreaksTaken.Should().Be(1);
    }

    [Fact]
    public void Complete_ActiveSession_CompletesAndRaisesEvent()
    {
        var session = CreateSpontaneousSession(); // 30 minutes
        session.Start(At.Utc(T0));
        var endedAt = T0.AddMinutes(20);

        session.Complete(At.Utc(endedAt), null);

        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().Be(At.Utc(endedAt));
        session.DomainEvents.OfType<SessionEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Complete_AlreadyCompleted_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));
        session.Complete(At.Utc(DateTime.UtcNow.AddMinutes(10)), null);

        var act = () => session.Complete(At.Utc(DateTime.UtcNow.AddMinutes(10)), null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetReflection_OnCompletedSession_TrimsAndStores()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));
        session.Complete(At.Utc(DateTime.UtcNow.AddMinutes(10)), null);

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
        session.Start(At.Utc(DateTime.UtcNow));
        session.Complete(At.Utc(DateTime.UtcNow.AddMinutes(10)), null);

        session.SetReflection(blank);

        session.Reflection.Should().BeNull();
    }

    [Fact]
    public void SetReflection_NotCompleted_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));

        var act = () => session.SetReflection("Too soon");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetReflection_OnMissedSession_Succeeds()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddHours(1))));

        session.SetReflection("Got distracted");

        session.Reflection.Should().Be("Got distracted");
    }

    [Fact]
    public void MarkPostSessionAcknowledged_OnCompletedSession_StampsTimestamp()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));
        session.Complete(At.Utc(DateTime.UtcNow.AddMinutes(10)), null);
        var acknowledgedAt = DateTime.UtcNow;

        session.MarkPostSessionAcknowledged(At.Utc(acknowledgedAt));

        session.PostSessionAcknowledgedAt.Should().Be(At.Utc(acknowledgedAt));
    }

    [Fact]
    public void MarkPostSessionAcknowledged_OnMissedSession_Succeeds()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddHours(1))));
        var acknowledgedAt = DateTime.UtcNow;

        session.MarkPostSessionAcknowledged(At.Utc(acknowledgedAt));

        session.PostSessionAcknowledgedAt.Should().Be(At.Utc(acknowledgedAt));
    }

    [Fact]
    public void MarkPostSessionAcknowledged_NotCompletedOrMissed_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));

        var act = () => session.MarkPostSessionAcknowledged(At.Utc(DateTime.UtcNow));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Miss_FromNotStarted_TransitionsAndRaisesEvent()
    {
        var blockId = Guid.NewGuid();
        var session = CreatePlannedSession(blockId);
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = scheduledStart.AddHours(1);

        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledEnd)));

        session.Status.Should().Be(SessionStatus.Missed);
        session.StartedAt.Should().Be(At.Utc(scheduledStart));
        session.EndedAt.Should().Be(At.Utc(scheduledEnd));
        var raised = session.DomainEvents.OfType<SessionMissedEvent>().Single();
        raised.SessionId.Should().Be(session.Id);
        raised.ScheduledStart.Should().Be(At.Utc(scheduledStart));
        raised.ScheduledEnd.Should().Be(At.Utc(scheduledEnd));
    }

    [Fact]
    public void Miss_WhenOriginSpontaneous_Throws()
    {
        var session = CreateSpontaneousSession();

        var act = () => session.Miss(new TimeSlot(At.Utc(DateTime.UtcNow), At.Utc(DateTime.UtcNow.AddHours(1))));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Miss_WhenAlreadyActive_Throws()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        session.Start(At.Utc(DateTime.UtcNow));

        var act = () => session.Miss(new TimeSlot(At.Utc(DateTime.UtcNow), At.Utc(DateTime.UtcNow.AddHours(1))));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Miss_WhenAlreadyMissed_Throws()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        var scheduledStart = DateTime.UtcNow;
        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddHours(1))));

        var act = () => session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddHours(1))));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetElapsedTime_WhenMissed_ReturnsZero_RegardlessOfStartEnd()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        var scheduledStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddHours(2))));

        session.GetElapsedTime(At.Utc(scheduledStart.AddHours(10))).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetElapsedTime_WhenCompleted_IsUnaffectedByMissedChange()
    {
        // Regression pin: the Missed short-circuit in GetElapsedTime must not
        // touch the Completed path's existing EndedAt-based calculation.
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(startedAt));
        session.Complete(At.Utc(startedAt.AddMinutes(15)), null);

        var elapsed = session.GetElapsedTime(At.Utc(startedAt.AddHours(5)));

        elapsed.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void GetElapsedTime_NotStarted_ReturnsNull()
    {
        var session = CreateSpontaneousSession();

        session.GetElapsedTime(At.Utc(DateTime.UtcNow)).Should().BeNull();
    }

    [Fact]
    public void GetElapsedTime_Active_MeasuresAgainstAsOf()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(startedAt));

        var elapsed = session.GetElapsedTime(At.Utc(startedAt.AddMinutes(10)));

        elapsed.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void GetElapsedTime_Completed_IgnoresAsOfAndUsesEndedAt()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(startedAt));
        session.Complete(At.Utc(startedAt.AddMinutes(15)), null);

        var elapsed = session.GetElapsedTime(At.Utc(startedAt.AddHours(5)));

        elapsed.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void GetRemainingTime_NotStarted_ReturnsNull()
    {
        var session = CreateSpontaneousSession();

        session.GetRemainingTime(At.Utc(DateTime.UtcNow)).Should().BeNull();
    }

    [Fact]
    public void GetRemainingTime_BeforeDurationElapsed_ReturnsDifference()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession(); // 30 minute duration
        session.Start(At.Utc(startedAt));

        var remaining = session.GetRemainingTime(At.Utc(startedAt.AddMinutes(10)));

        remaining.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void GetRemainingTime_PastDuration_ClampsToZero()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = CreateSpontaneousSession(); // 30 minute duration
        session.Start(At.Utc(startedAt));

        var remaining = session.GetRemainingTime(At.Utc(startedAt.AddHours(2)));

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
        session.Start(At.Utc(startedAt), At.PlannedWindowEnding(startedAt.AddMinutes(3)));

        var remaining = session.GetRemainingTime(At.Utc(startedAt.AddMinutes(1)));

        remaining.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllRaisedEvents()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(DateTime.UtcNow));

        session.ClearDomainEvents();

        session.DomainEvents.Should().BeEmpty();
    }

    // Gap 88: the time a running session occupies — what Focus placement must keep clear.

    [Fact]
    public void ReservedSlot_NotStarted_IsNull()
        => CreateSpontaneousSession().ReservedSlot.Should().BeNull();

    [Fact]
    public void ReservedSlot_Active_RunsFromStartToScheduledEnd()
    {
        var session = CreateSpontaneousSession();
        var startedAt = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

        session.Start(At.Utc(startedAt));

        session.ReservedSlot.Should().Be(new TimeSlot(At.Utc(2026, 9, 29, 10), At.Utc(2026, 9, 29, 10, 30)));
    }

    [Fact]
    public void ReservedSlot_Completed_IsNull()
    {
        var session = CreateSpontaneousSession();
        var startedAt = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        session.Start(At.Utc(startedAt));

        session.Complete(At.Utc(startedAt.AddMinutes(10)), null);

        session.ReservedSlot.Should().BeNull("a finished session occupies nothing any more");
    }

    // ── IsDueForAutoEnd: its time is up, and nothing has ended it yet (Gap 89 Slice A) ──

    private static readonly DateTime RunStart = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsDueForAutoEnd_BeforeItsEnd_IsFalse()
    {
        var session = CreateSpontaneousSession(); // 30 minutes
        session.Start(At.Utc(RunStart));

        session.IsDueForAutoEnd(At.Utc(RunStart.AddMinutes(29).AddSeconds(59))).Should().BeFalse();
    }

    [Fact]
    public void IsDueForAutoEnd_AtItsEnd_IsTrue()
    {
        // Half-open, like every TimeSlot: ended at its end.
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(RunStart));

        session.IsDueForAutoEnd(At.Utc(RunStart.AddMinutes(30))).Should().BeTrue();
    }

    [Fact]
    public void IsDueForAutoEnd_LongAfterItsEnd_IsTrue()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(RunStart));

        session.IsDueForAutoEnd(At.Utc(RunStart.AddDays(1))).Should().BeTrue("the app was closed through its end");
    }

    [Fact]
    public void IsDueForAutoEnd_OnceCompleted_IsFalse()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(RunStart));
        session.Complete(At.Utc(RunStart.AddMinutes(10)), null);

        session.IsDueForAutoEnd(At.Utc(RunStart.AddHours(1))).Should().BeFalse("something already ended it");
    }

    [Fact]
    public void IsDueForAutoEnd_NotStarted_IsFalse()
        => CreateSpontaneousSession().IsDueForAutoEnd(At.Utc(RunStart.AddDays(1))).Should().BeFalse();

    // ── CountedTime: how long an ended session counted for (Gap 95, decision 13) ──

    private static readonly DateTime BlockStart = new(2026, 1, 5, 13, 0, 0, DateTimeKind.Utc);

    private static FocusBlock BlockOf2Hours() =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(BlockStart, BlockStart.AddHours(2)));

    /// <summary>A planned session for <paramref name="block"/>, started and ended the way
    /// StartSession and EndSession leave both: the block carries the same two stamps.</summary>
    private static Session PlannedRun(FocusBlock block, double startMinutes, double endMinutes)
    {
        var session = CreatePlannedSession(block.Id);
        var startedAt = At.Utc(BlockStart.AddMinutes(startMinutes));
        session.Start(startedAt, block.Scheduled);
        block.MarkRitualCompleted(startedAt);
        session.Complete(At.Utc(BlockStart.AddMinutes(endMinutes)), block);
        return session;
    }

    // ── Complete with its block: one ending for "you ended it" and "its time ran out" (Gap 89 Slice A) ──

    [Fact]
    public void Complete_Planned_StampsItsBlockWithTheRecordedEnd_NotTheMomentNoticed()
    {
        // Noticed a day late (app closed): the session is capped at its planned end, and the
        // block's stamp is the same capped moment — a faithful copy (Gap 86).
        var block = BlockOf2Hours();
        var session = CreatePlannedSession(block.Id);
        session.Start(At.Utc(BlockStart), block.Scheduled);
        block.MarkRitualCompleted(At.Utc(BlockStart));

        session.Complete(At.Utc(BlockStart.AddDays(1)), block);

        session.EndedAt.Should().Be(At.Utc(BlockStart.AddHours(2)));
        block.EndedEarly.Should().BeFalse("its stamp is the capped end, so it ran to time");
        block.IsFinal(At.Utc(BlockStart.AddDays(1))).Should().BeTrue("the block knows its session ended");
    }

    [Fact]
    public void Complete_WithAnotherSessionsBlock_Throws_AndChangesNothing()
    {
        var block = BlockOf2Hours();
        var session = CreatePlannedSession(Guid.NewGuid());
        session.Start(At.Utc(BlockStart));
        block.MarkRitualCompleted(At.Utc(BlockStart));

        var act = () => session.Complete(At.Utc(BlockStart.AddMinutes(30)), block);

        act.Should().Throw<ArgumentException>();
        session.Status.Should().Be(SessionStatus.Active);
        block.IsFinal(At.Utc(BlockStart.AddMinutes(30))).Should().BeFalse();
    }

    [Fact]
    public void Complete_Spontaneous_WithABlock_Throws()
    {
        var block = BlockOf2Hours();
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(BlockStart));

        var act = () => session.Complete(At.Utc(BlockStart.AddMinutes(10)), block);

        act.Should().Throw<ArgumentException>();
        session.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public void Complete_WhenTheBlockRefusesTheStamp_LeavesTheSessionRunning()
    {
        // The block refuses a stamp before its Ritual (Gap 95). Asked first, so nothing is half done.
        var block = BlockOf2Hours();
        var session = CreatePlannedSession(block.Id);
        session.Start(At.Utc(BlockStart), block.Scheduled);

        var act = () => session.Complete(At.Utc(BlockStart.AddMinutes(30)), block);

        act.Should().Throw<InvalidOperationException>();
        session.Status.Should().Be(SessionStatus.Active);
        session.EndedAt.Should().BeNull();
    }

    // ── An ended session spans real time; Recorded is that span (Gap 89 Slice C, F7) ──

    [Fact]
    public void CanEndAt_AfterItsStart_IsTrue()
    {
        StartedTwoHourSession(BreakSchedule.None).CanEndAt(At.Utc(T0.AddMinutes(1))).Should().BeTrue();
    }

    [Fact]
    public void CanEndAt_PastItsPlannedEnd_IsTrue_SinceTheEndIsCappedThere()
    {
        StartedTwoHourSession(BreakSchedule.None).CanEndAt(At.Utc(T0.AddDays(1))).Should().BeTrue();
    }

    [Fact]
    public void CanEndAt_ItsStartInstant_IsFalse()
    {
        StartedTwoHourSession(BreakSchedule.None).CanEndAt(At.Utc(T0)).Should().BeFalse("it would have spanned no time");
    }

    [Fact]
    public void CanEndAt_BeforeItsStart_IsFalse()
    {
        // Only the debug clock, pushed back behind the start, can ask this.
        StartedTwoHourSession(BreakSchedule.None).CanEndAt(At.Utc(T0.AddMinutes(-5))).Should().BeFalse();
    }

    [Fact]
    public void CanEndAt_BeforeItHasStarted_IsFalse()
    {
        CreateSpontaneousSession().CanEndAt(At.Utc(T0)).Should().BeFalse();
    }

    [Fact]
    public void Complete_AtItsStartOrBefore_Throws_AndChangesNothing()
    {
        var block = BlockOf2Hours();
        var session = CreatePlannedSession(block.Id);
        session.Start(At.Utc(BlockStart), block.Scheduled);
        block.MarkRitualCompleted(At.Utc(BlockStart));

        var atStart = () => session.Complete(At.Utc(BlockStart), block);
        var before = () => session.Complete(At.Utc(BlockStart.AddMinutes(-5)), block);

        atStart.Should().Throw<InvalidOperationException>();
        before.Should().Throw<InvalidOperationException>();
        session.Status.Should().Be(SessionStatus.Active);
        session.EndedAt.Should().BeNull();
        block.IsFinal(At.Utc(BlockStart.AddMinutes(30))).Should().BeFalse("the block was not stamped");
    }

    [Fact]
    public void Recorded_BeforeItHasEnded_IsNull()
    {
        CreateSpontaneousSession().Recorded.Should().BeNull();
        StartedTwoHourSession(BreakSchedule.None).Recorded.Should().BeNull("it is still running");
    }

    [Fact]
    public void Recorded_Completed_IsItsStartToItsRecordedEnd()
    {
        var session = StartedTwoHourSession(BreakSchedule.None);
        session.Complete(At.Utc(T0.AddMinutes(40)), null);

        session.Recorded.Should().Be(At.Slot(T0, T0.AddMinutes(40)));
    }

    [Fact]
    public void Recorded_CompletedLate_EndsAtThePlannedEnd()
    {
        var session = StartedTwoHourSession(BreakSchedule.None);
        session.Complete(At.Utc(T0.AddDays(1)), null);

        session.Recorded.Should().Be(At.Slot(T0, T0.AddHours(2)));
    }

    [Fact]
    public void Recorded_Missed_IsItsBlocksWindow()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        var window = At.Slot(T0, T0.AddHours(1));
        session.Miss(window);

        session.Recorded.Should().Be(window);
    }

    [Fact]
    public void CountedTime_Spontaneous_IsTheWholeMinutesItRan()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(BlockStart));
        session.Complete(At.Utc(BlockStart.AddMinutes(20).AddSeconds(40)), null);

        session.CountedTime(originatingBlock: null).Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void CountedTime_PlannedWithinGrace_CountsTheBlocksCredit_NotTheMinutesItRan()
    {
        // Ritual 3 minutes in, ran to the end: grace credits the full 120, though it ran 117.
        var block = BlockOf2Hours();
        var session = PlannedRun(block, startMinutes: 3, endMinutes: 120);

        session.CountedTime(block).Should().Be(TimeSpan.FromMinutes(120));
    }

    [Fact]
    public void CountedTime_PlannedLateAndEndedEarly_CountsTheBlocksCredit()
    {
        var block = BlockOf2Hours();
        var session = PlannedRun(block, startMinutes: 20, endMinutes: 80);

        session.CountedTime(block).Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void CountedTime_Missed_IsZero()
    {
        var block = BlockOf2Hours();
        var session = CreatePlannedSession(block.Id);
        session.Miss(block.Scheduled);

        session.CountedTime(block).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CountedTime_PlannedWhoseBlockIsGone_FallsBackToTheMinutesItRan()
    {
        var session = CreatePlannedSession(Guid.NewGuid());
        session.Start(At.Utc(BlockStart));
        session.Complete(At.Utc(BlockStart.AddMinutes(25)), null);

        session.CountedTime(originatingBlock: null).Should().Be(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public void CountedTime_OfASessionNotEnded_Throws()
    {
        var session = CreateSpontaneousSession();
        session.Start(At.Utc(BlockStart));

        var act = () => session.CountedTime(originatingBlock: null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CountedTime_WithABlockThatIsNotItsOwn_Throws()
    {
        var session = PlannedRun(BlockOf2Hours(), startMinutes: 0, endMinutes: 60);

        var act = () => session.CountedTime(BlockOf2Hours());

        act.Should().Throw<ArgumentException>();
    }
}
