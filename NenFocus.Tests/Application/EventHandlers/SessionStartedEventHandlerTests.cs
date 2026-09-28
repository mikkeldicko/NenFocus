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
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SessionStartedEventHandlerTests() => _clock.UtcNow.Returns(Now);

    private SessionStartedEventHandler CreateHandler() => new(_synchronizer, _notifier, _sessions, _blocks, _presets, _clock);

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

    private static SessionStartedEvent StartedEvent(Session session) => new(session.Id, Guid.NewGuid(), "Goal", Now);

    [Fact]
    public async Task HandleAsync_SyncsEnforcementAtCurrentTime()
    {
        await CreateHandler().HandleAsync(StartedEvent(GivenSession()));

        await _synchronizer.Received(1).SyncAsync(Now, Arg.Any<CancellationToken>());
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
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddHours(1), "Thesis");

        await CreateHandler().HandleAsync(StartedEvent(GivenSession(block)));

        await _notifier.Received(1).NotifyAsync(new SessionStarted(true, "Thesis"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlannedSessionUntitledBlock_ReportsStartNamedAfterThePreset()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddHours(1));

        await CreateHandler().HandleAsync(StartedEvent(GivenSession(block)));

        await _notifier.Received(1).NotifyAsync(new SessionStarted(true, "Deep Work"), Arg.Any<CancellationToken>());
    }
}
