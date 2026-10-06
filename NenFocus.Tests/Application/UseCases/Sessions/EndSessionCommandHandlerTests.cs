using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class EndSessionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockedAttemptTracker _attemptTracker = Substitute.For<IBlockedAttemptTracker>();
    private readonly ISystemClock _clock = TestClock.Create();

    // Real instance, not mocked — it's a pure, dependency-free domain service, and the
    // grace-period-crediting tests below exist specifically to verify its actual
    // arithmetic end-to-end through this handler, not just that the handler calls it.

    public EndSessionCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _attemptTracker.GetAttemptsForSessionAsync(Arg.Any<Guid>()).Returns([]);
    }

    private EndSessionCommandHandler CreateHandler()
        => new(_sessions, _presets, _plannedBlocks, _attemptTracker, _clock);

    private static Session CreateActiveSession(Guid? originatingBlockId = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Deep work"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            originatingBlockId is null ? SessionOrigin.Spontaneous : SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);
        session.Start(At.Utc(Now.AddMinutes(-10)));
        return session;
    }

    [Fact]
    public async Task HandleAsync_SessionNotFound_Fails()
    {
        _sessions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_SessionNotStarted_Fails()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AlreadyCompleted_Fails()
    {
        var session = CreateActiveSession();
        session.Complete(At.Utc(Now));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_DuringABreakSlot_EndsAndRecordsTheBreakCutShort()
    {
        // Started 27 minutes ago with a 25-30 break: ending now falls mid-break.
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-27)), breakSchedule: new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromMinutes(60)),
            breakPresetName: "Classic");
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        result.PostSession!.BreaksTaken.Should().Be(1);
        session.BreakSchedule.Breaks.Single().End.Should().Be(TimeSpan.FromMinutes(27));
    }

    [Fact]
    public async Task HandleAsync_EndNoticedAfterPlannedEnd_RecordsPlannedEndNotNow()
    {
        // Regression (Gap 63): a 30-minute session whose end was only noticed 10 minutes
        // late (sleep, a restart, a clock jump) used to record 40 minutes.
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-40)));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        session.EndedAt.Should().Be(At.Utc(Now.AddMinutes(-10)));
        result.PostSession!.ActualDurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CompletesSessionAndReturnsPostSessionSummary()
    {
        var presetId = Guid.NewGuid();
        var session = CreateActiveSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _presets.GetByIdAsync(session.BlockPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(session.BlockPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        result.PostSession.Should().NotBeNull();
        result.PostSession!.PresetName.Should().Be("Deep Work");
        result.PostSession.Goal.Should().Be("Deep work");
        session.Status.Should().Be(SessionStatus.Completed);
        await _sessions.Received(1).SaveAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_GroupsBlockedAttemptsByResourceAndType()
    {
        var session = CreateActiveSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _attemptTracker.GetAttemptsForSessionAsync(session.Id).Returns(
        [
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, At.Utc(Now)),
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, At.Utc(Now)),
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "facebook.com", BlockedAttemptType.Website, At.Utc(Now))
        ]);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.BlockedAppAttempts.Should().ContainKey("chrome").WhoseValue.Should().Be(2);
        result.PostSession.BlockedWebsiteAttempts.Should().ContainKey("facebook.com").WhoseValue.Should().Be(1);
        result.PostSession.TotalBlockedAttempts.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_OriginatingBlockEndsLaterThanActualEnd_MarksSessionEndedWithoutMutatingEnd()
    {
        var blockId = Guid.NewGuid();
        var session = CreateActiveSession(blockId);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var originalEnd = Now.AddHours(1);
        var block = FocusBlock.Create(blockId, session.BlockPresetId,At.Slot(Now.AddMinutes(-10), originalEnd));
        block.MarkRitualCompleted(At.Utc(Now.AddMinutes(-10)));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        block.SessionEndedAt.Should().Be(At.Utc(Now));
        block.Scheduled.Should().Be(At.Slot(Now.AddMinutes(-10), originalEnd)); // original schedule preserved, not overwritten
        await _plannedBlocks.Received(1).SaveAsync(block, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SessionRanToTheBlocksEnd_StillMarksSessionEnded()
    {
        // The block's copy of "when did my session end" is always recorded, early or on
        // time — so it means the same as Session.EndedAt (Gap 86).
        var blockId = Guid.NewGuid();
        var session = CreateActiveSession(blockId);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var block = FocusBlock.Create(blockId, session.BlockPresetId,At.Slot(Now.AddMinutes(-10), Now));
        block.MarkRitualCompleted(At.Utc(Now.AddMinutes(-10)));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        block.SessionEndedAt.Should().Be(session.EndedAt);
        block.Scheduled.Should().Be(At.Slot(Now.AddMinutes(-10), Now));
        await _plannedBlocks.Received(1).SaveAsync(block, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_ReturnsBlankPresetName()
    {
        var session = CreateActiveSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _presets.GetByIdAsync(session.BlockPresetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.PresetName.Should().BeEmpty();
    }

    // ── Grace-period duration crediting ─────────────────────────────────────────
    // A Planned session's block runs blockStart..blockEnd == Now (ends exactly on
    // schedule); the Ritual completes some minutes after blockStart, which is also
    // when the session actually starts.

    private (FocusBlock block, Session session) PlannedSessionEndingNow(TimeSpan ritualDelayAfterBlockStart)
    {
        var blockId = Guid.NewGuid();
        var blockStart = Now.AddHours(-1);
        var blockEnd = Now;
        var ritualCompletedAt = blockStart + ritualDelayAfterBlockStart;

        var block = FocusBlock.Create(blockId, Guid.NewGuid(),At.Slot(blockStart, blockEnd));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(blockEnd - ritualCompletedAt),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        session.Start(At.Utc(ritualCompletedAt), At.PlannedWindowEnding(blockEnd));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        return (block, session);
    }

    [Theory]
    [InlineData(2)] // 2 minutes after block start — within the 5-minute grace period
    [InlineData(4)] // 4 minutes after — still within grace
    public async Task HandleAsync_Planned_RitualCompletedWithinGracePeriod_CreditsFullBlockDuration(int minutesAfterBlockStart)
    {
        var (_, session) = PlannedSessionEndingNow(TimeSpan.FromMinutes(minutesAfterBlockStart));

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.ActualDurationMinutes.Should().Be(60); // full 1-hour block, not (blockEnd - ritualCompletedAt)
    }

    [Fact]
    public async Task HandleAsync_Planned_RitualCompletedOutsideGracePeriod_CreditsActualElapsedOnly()
    {
        var (_, session) = PlannedSessionEndingNow(TimeSpan.FromMinutes(6)); // outside the 5-minute grace period

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.ActualDurationMinutes.Should().Be(54); // 60 - 6, no grace credit
    }

    [Fact]
    public async Task HandleAsync_Spontaneous_NeverGetsGracePeriodCredit()
    {
        // No originating block — no scheduled window exists to be graced against, so
        // this must always reflect actual elapsed time, however the session started.
        var session = CreateActiveSession(); // Spontaneous, started Now.AddMinutes(-10)
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.ActualDurationMinutes.Should().Be(10);
    }

    [Fact]
    public async Task HandleAsync_ShortBlock_CreditedDurationNeverExceedsFullBlockLength()
    {
        // A 2-minute block where the Ritual itself takes 1 minute — the exact scenario
        // that exposed the old, wrong "Planned = remaining time" fix. The grace-period
        // credit here must cap at the block's own full length (2 minutes), matching
        // exactly what StartSessionCommandHandler now computes as "Planned" for the
        // same block — Duration must never exceed Planned for a Planned session.
        var blockId = Guid.NewGuid();
        var blockStart = Now.AddMinutes(-1); // block: 1 min ago .. 1 min from now = 2 min total
        var blockEnd = Now.AddMinutes(1);
        var ritualCompletedAt = Now; // Ritual took the full first minute — still within 5-min grace

        var block = FocusBlock.Create(blockId, Guid.NewGuid(),At.Slot(blockStart, blockEnd));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(blockEnd - blockStart),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        session.Start(At.Utc(ritualCompletedAt), At.PlannedWindowEnding(blockEnd));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        _clock.SetNow(blockEnd); // session runs to its natural end

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.ActualDurationMinutes.Should().Be(2);
        result.PostSession.ActualDurationMinutes.Should().BeLessThanOrEqualTo(result.PostSession.PlannedDurationMinutes);
    }

    [Fact]
    public async Task HandleAsync_SubMinuteSession_FlooredToZeroCreditedMinutes()
    {
        // Session.GetElapsedTime floors to whole minutes at the single source — seconds
        // never matter for crediting, only for the Main Page's own live countdown, which
        // never reads this method. A 45-second spontaneous session honestly credits 0
        // minutes rather than preserving a fractional value nothing downstream can compare
        // against cleanly.
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Quick check"), new SessionDuration(TimeSpan.FromMinutes(1)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddSeconds(-45)));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

        result.PostSession!.ActualDuration.Should().Be(TimeSpan.Zero);
    }
}
