using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class SessionStartedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly IActiveSessionState _sessionState = Substitute.For<IActiveSessionState>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public SessionStartedEventHandlerTests() => _clock.SetNow(Now);

    private SessionStartedEventHandler CreateHandler() => new(_synchronizer, _notifier, _sessionState, _sessions, _blocks, _presets, _clock);

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
                block.BlockPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        }
        return session;
    }

    private static SessionStartedEvent StartedEvent(Session session) => new(session.Id, Guid.NewGuid(), "Goal", At.Utc(Now));

    [Fact]
    public async Task HandleAsync_SyncsEnforcementAtCurrentTime()
    {
        await CreateHandler().HandleAsync(StartedEvent(GivenSession()));

        await _synchronizer.Received(1).SyncAsync(At.Utc(Now), Arg.Any<CancellationToken>());
    }

    // StartSession no longer pushes a SessionDto: the app's view of the running session
    // reads the committed session itself, first (Gap 92, A3).
    [Fact]
    public async Task HandleAsync_RefreshesSessionStateBeforeSyncingEnforcement()
    {
        await CreateHandler().HandleAsync(StartedEvent(GivenSession()));

        Received.InOrder(() =>
        {
            _sessionState.Received().RefreshAsync(Arg.Any<CancellationToken>());
            _synchronizer.Received().SyncAsync(Arg.Any<Moment>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_SpontaneousSession_ReportsUnnamedStart()
    {
        await CreateHandler().HandleAsync(StartedEvent(GivenSession()));

        await _notifier.Received(1).NotifyAsync(new SessionStarted(false, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSession_ReportsStartNamedAfterItsBlock()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)), "Thesis");

        await CreateHandler().HandleAsync(StartedEvent(GivenSession(block)));

        await _notifier.Received(1).NotifyAsync(new SessionStarted(true, "Thesis"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSessionUntitledBlock_ReportsStartNamedAfterThePreset()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)));

        await CreateHandler().HandleAsync(StartedEvent(GivenSession(block)));

        await _notifier.Received(1).NotifyAsync(new SessionStarted(true, "Deep Work"), Arg.Any<CancellationToken>());
    }
}
