using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.AutoEndSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>
/// The clock's way of ending a session (Gap 89 Slice A): the heartbeat asks every tick, at its
/// one "now", and the running session ends only if its time is up. Real pipeline, real SQLite
/// (Gap 110). "Not yet" is the usual answer and is not a failure.
/// </summary>
public class AutoEndSessionTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public AutoEndSessionTests() => Clock.SetNow(Now);

    private Task<AutoEndSessionResult> AutoEndAsync(DateTime asOf) =>
        RunAsync(new AutoEndSessionCommand { AsOf = At.Utc(asOf) });

    /// <summary>A 30-minute session started 10 minutes before <see cref="Now"/>, so it ends at Now + 20.</summary>
    private static Session RunningSession(Guid? originatingBlockId = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Deep work"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            SeededFocusPresetId,
            originatingBlockId is null ? SessionOrigin.Spontaneous : SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);
        session.Start(At.Utc(Now.AddMinutes(-10)));
        return session;
    }

    [Fact]
    public async Task NoSessionRunning_EndsNothing()
    {
        var result = await AutoEndAsync(Now);

        result.EndedSessionId.Should().BeNull();
    }

    [Fact]
    public async Task ItsTimeIsNotUp_EndsNothing()
    {
        var session = RunningSession();
        await SeedAsync(session);

        var result = await AutoEndAsync(Now.AddMinutes(19));

        result.EndedSessionId.Should().BeNull();
        (await FindAsync<Session>(session.Id))!.Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public async Task ItsTimeIsUp_ItIsStoredCompleted_AtItsPlannedEnd()
    {
        // Asked a day late (the app was closed): recorded at its planned end, not the tick's moment.
        var session = RunningSession();
        await SeedAsync(session);

        var result = await AutoEndAsync(Now.AddDays(1));

        result.EndedSessionId.Should().Be(session.Id);
        var stored = (await FindAsync<Session>(session.Id))!;
        stored.Status.Should().Be(SessionStatus.Completed);
        stored.EndedAt.Should().Be(At.Utc(Now.AddMinutes(20)));
    }

    [Fact]
    public async Task ItsTimeIsUp_ItsPostSessionScreenIsShown_AfterTheSave()
    {
        // Same path as End early: SessionEndedEventHandler relays it once the end is saved.
        var session = RunningSession();
        await SeedAsync(session);
        var shown = new List<PostSessionDto>();
        Resolve<IRecapRelay>().PostSessionsReady += (_, screens) => shown.AddRange(screens);

        await AutoEndAsync(Now.AddMinutes(20));

        shown.Should().ContainSingle().Which.SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task ItsTimeIsNotUp_NoScreenIsShown()
    {
        await SeedAsync(RunningSession());
        var shown = new List<PostSessionDto>();
        Resolve<IRecapRelay>().PostSessionsReady += (_, screens) => shown.AddRange(screens);

        await AutoEndAsync(Now.AddMinutes(19));

        shown.Should().BeEmpty();
    }

    [Fact]
    public async Task APlannedSession_StampsItsBlock_WithTheSameEnd()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Now.AddMinutes(-10), Now.AddMinutes(20)));
        block.MarkRitualCompleted(At.Utc(Now.AddMinutes(-10)));
        var session = RunningSession(block.Id);
        await SeedAsync(block, session);

        await AutoEndAsync(Now.AddMinutes(20));

        var stored = (FocusBlock)(await FindAsync<PlannedBlock>(block.Id))!;
        stored.IsFinal(At.Utc(Now.AddMinutes(20))).Should().BeTrue("the block knows its session ended");
        stored.EndedEarly.Should().BeFalse("it ran to its end");
    }

    [Fact]
    public async Task AskedAgainAfterItEnded_EndsNothing()
    {
        // The heartbeat asks every tick; once ended, there's nothing running to end.
        var session = RunningSession();
        await SeedAsync(session);
        await AutoEndAsync(Now.AddMinutes(20));

        var again = await AutoEndAsync(Now.AddMinutes(25));

        again.EndedSessionId.Should().BeNull();
        (await FindAsync<Session>(session.Id))!.EndedAt.Should().Be(At.Utc(Now.AddMinutes(20)));
    }
}
