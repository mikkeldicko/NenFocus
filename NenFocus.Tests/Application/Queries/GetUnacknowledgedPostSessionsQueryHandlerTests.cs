using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetUnacknowledgedPostSessions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetUnacknowledgedPostSessionsQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockedAttemptTracker _attemptTracker = Substitute.For<IBlockedAttemptTracker>();
    private readonly IFocusBlockOutcomeClassifier _classifier = new FocusBlockOutcomeClassifier();

    public GetUnacknowledgedPostSessionsQueryHandlerTests()
    {
        _attemptTracker.GetAttemptsForSessionAsync(Arg.Any<Guid>()).Returns([]);
    }

    private GetUnacknowledgedPostSessionsQueryHandler CreateHandler()
        => new(_sessions, _presets, _plannedBlocks, _attemptTracker, _classifier);

    private static Session CreateCompletedSpontaneousSession(DateTime endedAt, TimeSpan elapsed)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(elapsed),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(endedAt - elapsed);
        session.Complete(endedAt);
        return session;
    }

    [Fact]
    public async Task HandleAsync_NoUnacknowledgedSessions_ReturnsEmpty()
    {
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CompletedSpontaneousSession_BuildsDtoWithActualElapsedMinutes()
    {
        var session = CreateCompletedSpontaneousSession(Now, TimeSpan.FromMinutes(10));
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _presets.GetByIdAsync(session.BlockPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(session.BlockPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result.Should().HaveCount(1);
        result[0].SessionId.Should().Be(session.Id);
        result[0].Status.Should().Be(SessionStatus.Completed);
        result[0].PresetName.Should().Be("Deep Work");
        result[0].ActualDurationMinutes.Should().Be(10);
    }

    [Fact]
    public async Task HandleAsync_MissedSession_ActualDurationIsZero()
    {
        var blockId = Guid.NewGuid();
        var scheduledStart = Now.AddHours(-2);
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("(missed — no goal set)"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        session.Miss(scheduledStart, scheduledStart.AddMinutes(30));
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result[0].Status.Should().Be(SessionStatus.Missed);
        result[0].ActualDurationMinutes.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_CompletedPlannedSession_AppliesGracePeriodCreditFromOriginatingBlock()
    {
        var blockId = Guid.NewGuid();
        var blockStart = Now.AddHours(-1);
        var blockEnd = Now;
        var ritualCompletedAt = blockStart.AddMinutes(3); // within 5-minute grace

        var block = FocusBlock.Create(blockId, Guid.NewGuid(), blockStart, blockEnd);
        block.MarkRitualCompleted(ritualCompletedAt);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);

        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(blockEnd - blockStart),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        session.Start(ritualCompletedAt, blockEnd);
        session.Complete(blockEnd);
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result[0].ActualDurationMinutes.Should().Be(60); // full block, grace period applied
    }

    [Fact]
    public async Task HandleAsync_OriginatingBlockNoLongerExists_FallsBackToActualElapsed()
    {
        var blockId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        session.Start(Now.AddMinutes(-10));
        session.Complete(Now);
        _plannedBlocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result[0].ActualDurationMinutes.Should().Be(10);
    }

    [Fact]
    public async Task HandleAsync_GroupsBlockedAttemptsByResourceAndType()
    {
        var session = CreateCompletedSpontaneousSession(Now, TimeSpan.FromMinutes(10));
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _attemptTracker.GetAttemptsForSessionAsync(session.Id).Returns(
        [
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, Now),
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, Now),
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "facebook.com", BlockedAttemptType.Website, Now)
        ]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result[0].BlockedAppAttempts.Should().ContainKey("chrome").WhoseValue.Should().Be(2);
        result[0].BlockedWebsiteAttempts.Should().ContainKey("facebook.com").WhoseValue.Should().Be(1);
        result[0].TotalBlockedAttempts.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_MultipleSessions_PreservesRepositoryOrdering()
    {
        var earlier = CreateCompletedSpontaneousSession(Now.AddHours(-2), TimeSpan.FromMinutes(5));
        var later = CreateCompletedSpontaneousSession(Now, TimeSpan.FromMinutes(5));
        _sessions.GetUnacknowledgedPostSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[earlier, later]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostSessionsQuery());

        result.Should().HaveCount(2);
        result[0].SessionId.Should().Be(earlier.Id);
        result[1].SessionId.Should().Be(later.Id);
    }
}
