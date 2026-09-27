using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class SessionEndedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 11, 0, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SessionEndedEventHandlerTests() => _clock.UtcNow.Returns(Now);

    private SessionEndedEventHandler CreateHandler() => new(_synchronizer, _notifications, _sessionState, _clock);

    [Fact]
    public async Task HandleAsync_ClearsSessionStateSyncsEnforcementAndNotifies()
    {
        // Gap 62: previously removed ALL enforcement, unblocking a still-running
        // Background Block until the next heartbeat.
        await CreateHandler().HandleAsync(new SessionEndedEvent(Guid.NewGuid(), Now));

        _sessionState.Received(1).Clear();
        await _synchronizer.Received(1).SyncAsync(Now, Arg.Any<CancellationToken>());
        await _notifications.Received(1).CancelAllRemindersAsync(Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync(
            Arg.Is<string>(t => t.Contains("complete")), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ClearsSessionStateBeforeSyncingEnforcement()
    {
        // Ordering matters: sessionState.Clear() must happen before enforcement is synced
        // so ScheduledEnforcementService never observes a null ActiveSession while the
        // DB still shows the session as Active (see the handler's own comment).
        await CreateHandler().HandleAsync(new SessionEndedEvent(Guid.NewGuid(), Now));

        Received.InOrder(() =>
        {
            _sessionState.Received().Clear();
            _synchronizer.Received().SyncAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        });
    }
}
