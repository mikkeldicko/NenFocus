using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class SessionTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private static Session StartedSpontaneous(DateTime startedAt, TimeSpan length)
    {
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(startedAt, startedAt + length));
        return Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(startedAt));
    }

    [Fact]
    public void Start_Spontaneous_IsActiveOnItsWalkInBlock()
    {
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddMinutes(30)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0));

        session.OriginatingBlockId.Should().Be(block.Id);
        block.BookingType.Should().Be(FocusBlockBookingType.Spontaneous);
        session.Status.Should().Be(SessionStatus.Active);
        session.TemporaryBlockedApps.Should().BeEmpty();
        session.TemporaryBlockedSites.Should().BeEmpty();
    }

    [Fact]
    public void Start_Planned_IsOnItsPlannedBlock()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(1)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0));

        session.OriginatingBlockId.Should().Be(block.Id);
        block.BookingType.Should().Be(FocusBlockBookingType.Planned);
    }

    [Fact]
    public void Start_ActivatesAndRaisesEvent()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(startedAt, startedAt.AddMinutes(30)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(startedAt));

        session.Status.Should().Be(SessionStatus.Active);
        session.StartedAt.Should().Be(At.Utc(startedAt));
        session.EndAt.Should().Be(At.Utc(startedAt.AddMinutes(30)));
        session.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SessionStartedEvent>();

        var raised = (SessionStartedEvent)session.DomainEvents.Single();
        raised.SessionId.Should().Be(session.Id);
        raised.BlockPresetId.Should().Be(block.BlockPresetId);
        raised.Goal.Should().Be(session.Goal.Value);
    }

    [Fact]
    public void Start_Planned_EndsAtItsBlocksScheduledEnd()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(startedAt, startedAt.AddMinutes(3)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(startedAt));

        session.EndAt.Should().Be(At.Utc(startedAt.AddMinutes(3)));
    }

    // ── Break schedule (Gap 63) ──────────────────────────────────────────────

    private static BreakSchedule OneShortBreak(int startMinute = 25, int endMinute = 30) =>
        new([new SessionBreak(BreakKind.ShortBreak,
            TimeSpan.FromMinutes(startMinute), TimeSpan.FromMinutes(endMinute))]);

    private static Session StartedTwoHourSession(BreakSchedule schedule, string? presetName = "Classic")
    {
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(2)));
        return Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0),
            breakSchedule: schedule, breakPresetName: presetName);
    }

    [Fact]
    public void Start_WithoutSchedule_HasNoBreaksAndNoLabel()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromHours(2));

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
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddMinutes(30)));

        var act = () => Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0),
            breakSchedule: OneShortBreak(25, 30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Start_ScheduleMustFitTheBlocksSlot()
    {
        // A planned block of 20 minutes can't fit a break that ends at 30 — the schedule is
        // laid over the block's own slot (Gap 114, D6).
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddMinutes(20)));

        var act = () => Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0),
            breakSchedule: OneShortBreak(25, 30));

        act.Should().Throw<ArgumentException>();
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
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(2)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0),
            breakSchedule: OneShortBreak(), keepBlockingDuringBreaks: keepBlocking);

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
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));
        var endedAt = T0.AddMinutes(20);

        session.Complete(At.Utc(endedAt), null);

        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().Be(At.Utc(endedAt));
        session.DomainEvents.OfType<SessionEndedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Complete_AlreadyCompleted_Throws()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(T0.AddMinutes(10)), null);

        var act = () => session.Complete(At.Utc(T0.AddMinutes(20)), null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetReflection_OnCompletedSession_TrimsAndStores()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(T0.AddMinutes(10)), null);

        session.SetReflection("  Went well  ");

        session.Reflection.Text.Should().Be("Went well");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetReflection_BlankValue_ClearsReflection(string? blank)
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(T0.AddMinutes(10)), null);

        session.SetReflection(blank);

        session.Reflection.Text.Should().BeNull();
    }

    [Fact]
    public void SetReflection_NotCompleted_Throws()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));

        var act = () => session.SetReflection("Too soon");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkMissed_BlockCanTakeAReflection()
    {
        // A missed run's note lives on the missed block (Gap 114, D3).
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(1)));
        block.MarkMissed(At.Utc(T0.AddHours(1)));

        block.SetReflection("Got distracted");

        block.Reflection.Text.Should().Be("Got distracted");
    }

    [Fact]
    public void MarkPostSessionAcknowledged_OnCompletedSession_StampsTimestamp()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(T0.AddMinutes(10)), null);
        var acknowledgedAt = DateTime.UtcNow;

        session.MarkPostSessionAcknowledged(At.Utc(acknowledgedAt));

        session.Reflection.AcknowledgedAt.Should().Be(At.Utc(acknowledgedAt));
    }

    [Fact]
    public void MarkMissed_BlockCanBeAcknowledged()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(1)));
        block.MarkMissed(At.Utc(T0.AddHours(1)));
        var acknowledgedAt = DateTime.UtcNow;

        block.MarkPostSessionAcknowledged(At.Utc(acknowledgedAt));

        block.Reflection.AcknowledgedAt.Should().Be(At.Utc(acknowledgedAt));
    }

    [Fact]
    public void MarkPostSessionAcknowledged_NotCompleted_Throws()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));

        var act = () => session.MarkPostSessionAcknowledged(At.Utc(DateTime.UtcNow));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkMissed_FreezesTheBlockAsMissed()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(1)));

        block.MarkMissed(At.Utc(T0.AddHours(1)));

        block.Outcome(At.Utc(T0.AddHours(1))).Should().Be(FocusBlockOutcome.Missed);
        block.Result.Should().Be(new FocusBlockResult(FocusBlockOutcome.Missed, null));
    }

    [Fact]
    public void MarkMissed_OnABlockWhoseRitualIsDone_Throws()
    {
        // Its session already started it — only an unresolved block can be marked missed.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(1)));
        Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(T0));

        var act = () => block.MarkMissed(At.Utc(T0.AddHours(1)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkMissed_Twice_Throws()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(T0, T0.AddHours(1)));
        block.MarkMissed(At.Utc(T0.AddHours(1)));

        var act = () => block.MarkMissed(At.Utc(T0.AddHours(1)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetElapsedTime_WhenCompleted_IsUnaffectedByMissedChange()
    {
        // Regression pin: the Missed short-circuit in GetElapsedTime must not
        // touch the Completed path's existing EndedAt-based calculation.
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(startedAt.AddMinutes(15)), null);

        var elapsed = session.GetElapsedTime(At.Utc(startedAt.AddHours(5)));

        elapsed.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void GetElapsedTime_Active_MeasuresAgainstAsOf()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));

        var elapsed = session.GetElapsedTime(At.Utc(startedAt.AddMinutes(10)));

        elapsed.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void GetElapsedTime_Completed_IgnoresAsOfAndUsesEndedAt()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(startedAt.AddMinutes(15)), null);

        var elapsed = session.GetElapsedTime(At.Utc(startedAt.AddHours(5)));

        elapsed.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void GetRemainingTime_BeforeDurationElapsed_ReturnsDifference()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));

        var remaining = session.GetRemainingTime(At.Utc(startedAt.AddMinutes(10)));

        remaining.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void GetRemainingTime_PastDuration_ClampsToZero()
    {
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));

        var remaining = session.GetRemainingTime(At.Utc(startedAt.AddHours(2)));

        remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetRemainingTime_Planned_IsTheBlocksEnd()
    {
        // A planned session's remaining time tracks its block's real end — there is no separate
        // duration to go stale against (Gap 114, D6).
        var startedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(startedAt, startedAt.AddMinutes(3)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(startedAt));

        var remaining = session.GetRemainingTime(At.Utc(startedAt.AddMinutes(1)));

        remaining.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllRaisedEvents()
    {
        var session = StartedSpontaneous(T0, TimeSpan.FromMinutes(30));

        session.ClearDomainEvents();

        session.DomainEvents.Should().BeEmpty();
    }

    // Gap 88: the time a running session occupies — what Focus placement must keep clear.

    [Fact]
    public void ReservedSlot_Active_RunsFromStartToScheduledEnd()
    {
        var startedAt = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));

        session.ReservedSlot.Should().Be(new TimeSlot(At.Utc(2026, 9, 29, 10), At.Utc(2026, 9, 29, 10, 30)));
    }

    [Fact]
    public void ReservedSlot_Completed_IsNull()
    {
        var startedAt = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        var session = StartedSpontaneous(startedAt, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(startedAt.AddMinutes(10)), null);

        session.ReservedSlot.Should().BeNull("a finished session occupies nothing any more");
    }

    // ── IsDueForAutoEnd: its time is up, and nothing has ended it yet (Gap 89 Slice A) ──

    private static readonly DateTime RunStart = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsDueForAutoEnd_BeforeItsEnd_IsFalse()
    {
        var session = StartedSpontaneous(RunStart, TimeSpan.FromMinutes(30));

        session.IsDueForAutoEnd(At.Utc(RunStart.AddMinutes(29).AddSeconds(59))).Should().BeFalse();
    }

    [Fact]
    public void IsDueForAutoEnd_AtItsEnd_IsTrue()
    {
        // Half-open, like every TimeSlot: ended at its end.
        var session = StartedSpontaneous(RunStart, TimeSpan.FromMinutes(30));

        session.IsDueForAutoEnd(At.Utc(RunStart.AddMinutes(30))).Should().BeTrue();
    }

    [Fact]
    public void IsDueForAutoEnd_LongAfterItsEnd_IsTrue()
    {
        var session = StartedSpontaneous(RunStart, TimeSpan.FromMinutes(30));

        session.IsDueForAutoEnd(At.Utc(RunStart.AddDays(1))).Should().BeTrue("the app was closed through its end");
    }

    [Fact]
    public void IsDueForAutoEnd_OnceCompleted_IsFalse()
    {
        var session = StartedSpontaneous(RunStart, TimeSpan.FromMinutes(30));
        session.Complete(At.Utc(RunStart.AddMinutes(10)), null);

        session.IsDueForAutoEnd(At.Utc(RunStart.AddHours(1))).Should().BeFalse("something already ended it");
    }

    // ── CountedTime: how long an ended session counted for (Gap 95, decision 13) ──

    private static readonly DateTime BlockStart = new(2026, 1, 5, 13, 0, 0, DateTimeKind.Utc);

    private static FocusBlock BlockOf2Hours() =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(BlockStart, BlockStart.AddHours(2)));

    /// <summary>A planned session for <paramref name="block"/>, started and ended the way
    /// StartSession and EndSession leave both: Start stamps the Ritual, Complete stamps the end.</summary>
    private static Session PlannedRun(FocusBlock block, double startMinutes, double endMinutes)
    {
        var startedAt = At.Utc(BlockStart.AddMinutes(startMinutes));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, startedAt);
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
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(BlockStart));

        session.Complete(At.Utc(BlockStart.AddDays(1)), block);

        session.EndedAt.Should().Be(At.Utc(BlockStart.AddHours(2)));
        block.EndedEarly.Should().BeFalse("its stamp is the capped end, so it ran to time");
        block.IsFinal(At.Utc(BlockStart.AddDays(1))).Should().BeTrue("the block knows its session ended");
    }

    [Fact]
    public void Complete_WithAnotherSessionsBlock_Throws_AndChangesNothing()
    {
        var block = BlockOf2Hours();
        var otherBlock = BlockOf2Hours();
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), otherBlock, At.Utc(BlockStart));

        var act = () => session.Complete(At.Utc(BlockStart.AddMinutes(30)), block);

        act.Should().Throw<ArgumentException>();
        session.Status.Should().Be(SessionStatus.Active);
        block.IsFinal(At.Utc(BlockStart.AddMinutes(30))).Should().BeFalse();
    }

    [Fact]
    public void Complete_Spontaneous_WithAnotherBlock_Throws()
    {
        var block = BlockOf2Hours();
        var walkIn = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(BlockStart, BlockStart.AddMinutes(30)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), walkIn, At.Utc(BlockStart));

        var act = () => session.Complete(At.Utc(BlockStart.AddMinutes(10)), block);

        act.Should().Throw<ArgumentException>();
        session.Status.Should().Be(SessionStatus.Active);
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
    public void Complete_AtItsStartOrBefore_Throws_AndChangesNothing()
    {
        var block = BlockOf2Hours();
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(BlockStart));

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
        StartedSpontaneous(T0, TimeSpan.FromMinutes(30)).Recorded.Should().BeNull();
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
    public void CountedTime_Spontaneous_IsTheWholeMinutesItRan()
    {
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(BlockStart, BlockStart.AddMinutes(30)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(BlockStart));
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
    public void CountedTime_PlannedWhoseBlockIsGone_FallsBackToTheMinutesItRan()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(BlockStart, BlockStart.AddHours(2)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(BlockStart));
        session.Complete(At.Utc(BlockStart.AddMinutes(25)), null);

        session.CountedTime(originatingBlock: null).Should().Be(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public void CountedTime_OfASessionNotEnded_Throws()
    {
        var session = StartedSpontaneous(BlockStart, TimeSpan.FromMinutes(30));

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
