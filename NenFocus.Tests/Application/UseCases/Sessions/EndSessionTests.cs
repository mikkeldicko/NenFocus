using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>
/// Ending a session, run as the app runs it: real pipeline, real SQLite (Gap 110). The session
/// completes (capped at its planned end, Gap 63), a planned session's block records when it ended
/// (Gap 86), and the Post-Session summary credits the block's grace period (Gap 95).
/// </summary>
public class EndSessionTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public EndSessionTests() => Clock.SetNow(Now);

    private Task<EndSessionResult> EndAsync(Guid sessionId) =>
        RunAsync(new EndSessionCommand { SessionId = sessionId });

    private static Session ActiveSession(Guid? originatingBlockId = null, Guid? presetId = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Deep work"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId ?? SeededFocusPresetId,
            originatingBlockId is null ? SessionOrigin.Spontaneous : SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);
        session.Start(At.Utc(Now.AddMinutes(-10)));
        return session;
    }

    /// <summary>Ends it and returns the Post-Session screen relayed for it — only after the commit, by
    /// SessionEndedEventHandler, the one path to that screen (Gap 89 Slice A).</summary>
    private async Task<PostSessionDto> EndedAsync(Session session)
    {
        var shown = new List<PostSessionDto>();
        Resolve<IRecapRelay>().PostSessionsReady += (_, screens) => shown.AddRange(screens);

        var result = await EndAsync(session.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        return shown.Should().ContainSingle().Subject;
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownSession_IsRefused()
    {
        var result = await EndAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ASessionNotStarted_IsRefused()
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            SeededFocusPresetId, SessionOrigin.Spontaneous);
        await SeedAsync(session);

        var result = await EndAsync(session.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<Session>(session.Id))!.Status.Should().Be(SessionStatus.NotStarted);
    }

    [Fact]
    public async Task AnAlreadyCompletedSession_IsRefused()
    {
        var session = ActiveSession();
        session.Complete(At.Utc(Now.AddMinutes(-5)), null);
        await SeedAsync(session);

        var shown = new List<PostSessionDto>();
        Resolve<IRecapRelay>().PostSessionsReady += (_, screens) => shown.AddRange(screens);

        var result = await EndAsync(session.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<Session>(session.Id))!.EndedAt.Should().Be(At.Utc(Now.AddMinutes(-5)));
        shown.Should().BeEmpty("a refused end saves nothing, so no screen follows");
    }

    // ── What gets stored ─────────────────────────────────────────────────────

    [Fact]
    public async Task ARunningSession_IsStoredCompleted_AndSummarised()
    {
        var session = ActiveSession(presetId: await FocusPresetAsync("Deep Work"));
        await SeedAsync(session);

        var shown = await EndedAsync(session);

        shown.PresetName.Should().Be("Deep Work");
        shown.Goal.Should().Be("Deep work");
        var stored = (await FindAsync<Session>(session.Id))!;
        stored.Status.Should().Be(SessionStatus.Completed);
        stored.EndedAt.Should().Be(At.Utc(Now));
    }

    [Fact]
    public async Task EndedDuringABreakSlot_StoresTheBreakCutShort()
    {
        // Started 27 minutes ago with a 25–30 break: ending now falls mid-break.
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            SeededFocusPresetId, SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-27)), breakSchedule: new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromMinutes(60)),
            breakPresetName: "Classic");
        await SeedAsync(session);

        var shown = await EndedAsync(session);

        shown.BreaksTaken.Should().Be(1);
        (await FindAsync<Session>(session.Id))!.BreakSchedule.Breaks.Single().End.Should().Be(TimeSpan.FromMinutes(27));
    }

    [Fact]
    public async Task AnEndNoticedAfterThePlannedEnd_StoresThePlannedEnd_NotNow()
    {
        // Regression (Gap 63): a 30-minute session whose end was only noticed 10 minutes late
        // (sleep, a restart, a clock jump) used to record 40 minutes.
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            SeededFocusPresetId, SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-40)));
        await SeedAsync(session);

        var shown = await EndedAsync(session);

        (await FindAsync<Session>(session.Id))!.EndedAt.Should().Be(At.Utc(Now.AddMinutes(-10)));
        shown.ActualDurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task BlockedAttempts_AreGroupedByResourceAndType()
    {
        var session = ActiveSession();
        await SeedAsync(session,
            BlockedAttempt.ForApp(Guid.NewGuid(), session.Id, new BlockedApp("chrome"), At.Utc(Now.AddMinutes(-9))),
            BlockedAttempt.ForApp(Guid.NewGuid(), session.Id, new BlockedApp("chrome"), At.Utc(Now.AddMinutes(-5))),
            BlockedAttempt.ForWebsite(Guid.NewGuid(), session.Id, new Website("facebook.com"), At.Utc(Now.AddMinutes(-3))));

        var shown = await EndedAsync(session);

        shown.BlockedAppAttempts.Should().ContainKey("chrome").WhoseValue.Should().Be(2);
        shown.BlockedWebsiteAttempts.Should().ContainKey("facebook.com").WhoseValue.Should().Be(1);
        shown.TotalBlockedAttempts.Should().Be(3);
    }

    [Fact]
    public async Task AnUnknownPreset_GivesABlankPresetName()
    {
        var session = ActiveSession(presetId: Guid.NewGuid());
        await SeedAsync(session);

        var shown = await EndedAsync(session);

        shown.PresetName.Should().BeEmpty();
    }

    // ── The originating block records when its session ended (Gap 86) ───────

    private async Task<(FocusBlock Block, Session Session)> PlannedAsync(DateTime blockEnd)
    {
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Now.AddMinutes(-10), blockEnd));
        block.MarkRitualCompleted(At.Utc(Now.AddMinutes(-10)));
        var session = ActiveSession(block.Id);
        await SeedAsync(block, session);
        return (block, session);
    }

    [Fact]
    public async Task EndedBeforeItsBlock_TheBlockRecordsTheEnd_WithoutChangingItsSchedule()
    {
        var (block, session) = await PlannedAsync(blockEnd: Now.AddHours(1));

        await EndedAsync(session);

        var stored = (FocusBlock)(await FindAsync<PlannedBlock>(block.Id))!;
        stored.EndedEarly.Should().BeTrue();
        stored.WorkSlot().Should().Be(At.Slot(Now.AddMinutes(-10), Now)); // stamped at the session's end
        stored.Scheduled.Should().Be(At.Slot(Now.AddMinutes(-10), Now.AddHours(1))); // schedule kept, not overwritten
    }

    [Fact]
    public async Task RunToItsBlocksEnd_TheBlockStillRecordsTheEnd()
    {
        // Always recorded, early or on time — so it means the same as Session.EndedAt (Gap 86).
        var (block, session) = await PlannedAsync(blockEnd: Now);

        await EndedAsync(session);

        var stored = (FocusBlock)(await FindAsync<PlannedBlock>(block.Id))!;
        stored.IsFinal(At.Utc(Now)).Should().BeTrue(); // a block with a Ritual is final only once its session ended
        stored.EndedEarly.Should().BeFalse();
        stored.Scheduled.Should().Be(At.Slot(Now.AddMinutes(-10), Now));
    }

    // ── Grace-period crediting ───────────────────────────────────────────────
    // A planned block runs blockStart..blockEnd; the Ritual completes some minutes after
    // blockStart, which is when the session starts.

    private async Task<Session> PlannedSessionAsync(DateTime blockStart, DateTime blockEnd, DateTime ritualCompletedAt, TimeSpan duration)
    {
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(blockStart, blockEnd));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(duration),
            SeededFocusPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(At.Utc(ritualCompletedAt), At.PlannedWindowEnding(blockEnd));
        await SeedAsync(block, session);
        return session;
    }

    /// <summary>A one-hour block ending now, its Ritual done <paramref name="ritualDelay"/> after it started.</summary>
    private Task<Session> PlannedSessionEndingNowAsync(TimeSpan ritualDelay) =>
        PlannedSessionAsync(Now.AddHours(-1), Now, Now.AddHours(-1) + ritualDelay, TimeSpan.FromHours(1) - ritualDelay);

    [Theory]
    [InlineData(2)] // within the 5-minute grace period
    [InlineData(4)]
    public async Task ARitualDoneWithinTheGracePeriod_CreditsTheWholeBlock(int minutesLate)
    {
        var session = await PlannedSessionEndingNowAsync(TimeSpan.FromMinutes(minutesLate));

        var shown = await EndedAsync(session);

        shown.ActualDurationMinutes.Should().Be(60); // the whole block, not blockEnd - ritual
    }

    [Fact]
    public async Task ARitualDoneAfterTheGracePeriod_CreditsOnlyWhatRan()
    {
        var session = await PlannedSessionEndingNowAsync(TimeSpan.FromMinutes(6));

        var shown = await EndedAsync(session);

        shown.ActualDurationMinutes.Should().Be(54);
    }

    [Fact]
    public async Task ASpontaneousSession_NeverGetsGraceCredit()
    {
        // No block, so no window to be graced against.
        var session = ActiveSession(); // started 10 minutes ago
        await SeedAsync(session);

        var shown = await EndedAsync(session);

        shown.ActualDurationMinutes.Should().Be(10);
    }

    [Fact]
    public async Task AShortBlocksCredit_NeverExceedsTheBlock()
    {
        // A 2-minute block whose Ritual took its first minute — still within grace. The credit caps
        // at the block's own length, which is also what Start called "Planned".
        var blockStart = Now.AddMinutes(-1);
        var blockEnd = Now.AddMinutes(1);
        var session = await PlannedSessionAsync(blockStart, blockEnd, ritualCompletedAt: Now, duration: blockEnd - blockStart);
        Clock.SetNow(blockEnd); // runs to its natural end

        var shown = await EndedAsync(session);

        shown.ActualDurationMinutes.Should().Be(2);
        shown.ActualDurationMinutes.Should().BeLessThanOrEqualTo(shown.PlannedDurationMinutes);
    }

    [Fact]
    public async Task ASubMinuteSession_CreditsZero()
    {
        // Credit is whole minutes; a 45-second session honestly credits none.
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Quick check"), new SessionDuration(TimeSpan.FromMinutes(1)),
            SeededFocusPresetId, SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddSeconds(-45)));
        await SeedAsync(session);

        var shown = await EndedAsync(session);

        shown.ActualDuration.Should().Be(TimeSpan.Zero);
    }
}
