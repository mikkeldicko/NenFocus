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
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SessionEndedEventHandlerTests() => _clock.UtcNow.Returns(Now);

    private SessionEndedEventHandler CreateHandler() =>
        new(_synchronizer, _notifier, _sessionState, _sessions, _blocks, _presets, _clock);

    private Session GivenSession(FocusBlock? block = null)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(),
            block is null ? SessionOrigin.Spontaneous : SessionOrigin.Planned, block?.Id);
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        if (block is not null)
        {
            _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
            _presets.GetByIdAsync(block.BlockPresetId, Arg.Any<CancellationToken>()).Returns(BlockPreset.Create(
                block.BlockPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        }
        return session;
    }

    [Fact]
    public async Task HandleAsync_ClearsSessionStateSyncsEnforcementAndReportsCompletion()
    {
        // Gap 62: previously removed ALL enforcement, unblocking a still-running
        // Background Block until the next heartbeat.
        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession().Id, Now));

        _sessionState.Received(1).Clear();
        await _synchronizer.Received(1).SyncAsync(Now, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyAsync(new SessionCompleted(false, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSession_ReportsCompletionNamedAfterItsBlock()
    {
        // The block stays quiet about this moment (FocusBlock.AnnouncesOwnEnd) — this is the one toast.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1), Now, "Thesis");

        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession(block).Id, Now));

        await _notifier.Received(1).NotifyAsync(new SessionCompleted(true, "Thesis"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSessionUntitledBlock_ReportsCompletionNamedAfterThePreset()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1), Now);

        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession(block).Id, Now));

        await _notifier.Received(1).NotifyAsync(new SessionCompleted(true, "Deep Work"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ClearsSessionStateBeforeSyncingEnforcement()
    {
        // Ordering matters: sessionState.Clear() must happen before enforcement is synced
        // so ScheduledEnforcementService never observes a null ActiveSession while the
        // DB still shows the session as Active (see the handler's own comment).
        await CreateHandler().HandleAsync(new SessionEndedEvent(GivenSession().Id, Now));

        Received.InOrder(() =>
        {
            _sessionState.Received().Clear();
            _synchronizer.Received().SyncAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        });
    }
}
