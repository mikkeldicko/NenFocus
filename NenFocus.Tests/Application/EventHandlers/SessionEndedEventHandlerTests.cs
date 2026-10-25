using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class SessionEndedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 11, 0, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly IActiveSessionState _sessionState = Substitute.For<IActiveSessionState>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IBlockedAttemptRepository _attempts = Substitute.For<IBlockedAttemptRepository>();
    private readonly IRecapRelay _recaps = Substitute.For<IRecapRelay>();
    private readonly ISystemClock _clock = TestClock.Create();

    public SessionEndedEventHandlerTests() => _clock.SetNow(Now);

    private SessionEndedEventHandler CreateHandler() =>
        new(_synchronizer, _notifier, _sessionState, _sessions, _blocks, _presets, _attempts, _recaps, _clock);

    private Session GivenSession(FocusBlock? block = null)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(),
            block is null ? SessionOrigin.Spontaneous : SessionOrigin.Planned, block?.Id);
        // The event follows an end, so the session has a start and an end, as stored.
        session.Start(At.Utc(Now.AddHours(-1)));
        session.Complete(At.Utc(Now), null);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        if (block is not null)
        {
            _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
            _presets.GetByIdAsync(block.BlockPresetId, Arg.Any<CancellationToken>()).Returns(BlockPreset.Create(
                block.BlockPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        }
        return session;
    }

    [Fact]
    public async Task HandleAsync_RefreshesSessionStateSyncsEnforcementAndReportsCompletion()
    {
        // Gap 62: previously removed ALL enforcement, unblocking a still-running
        // Background Block until the next heartbeat.
        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession().Id, At.Utc(Now)));

        await _sessionState.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
        await _synchronizer.Received(1).SyncAsync(At.Utc(Now), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new SessionCompleted(false, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSession_ReportsCompletionNamedAfterItsBlock()
    {
        // The block stays quiet about this moment (FocusBlock.AnnouncesOwnEnd) — this is the one toast.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-1), Now), "Thesis");

        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession(block).Id, At.Utc(Now)));

        await _notifier.Received(1).NotifyAsync(new SessionCompleted(true, "Thesis"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSessionUntitledBlock_ReportsCompletionNamedAfterThePreset()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-1), Now));

        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession(block).Id, At.Utc(Now)));

        await _notifier.Received(1).NotifyAsync(new SessionCompleted(true, "Deep Work"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RefreshesSessionStateBeforeSyncingEnforcement()
    {
        // Ordering matters: the session state refreshes before enforcement is synced
        // so the heartbeat never observes a null ActiveSession while the
        // DB still shows the session as Active (see the handler's own comment).
        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession().Id, At.Utc(Now)));

        Received.InOrder(() =>
        {
            _sessionState.Received().RefreshAsync(Arg.Any<CancellationToken>());
            _synchronizer.Received().SyncAsync(Arg.Any<Moment>(), Arg.Any<CancellationToken>());
        });
    }
}
