using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class SessionStartedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SessionStartedEventHandlerTests() => _clock.UtcNow.Returns(Now);

    private SessionStartedEventHandler CreateHandler() => new(_synchronizer, _notifications, _clock);

    [Fact]
    public async Task HandleAsync_SyncsEnforcementAtCurrentTime()
    {
        await CreateHandler().HandleAsync(
            new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", Now));

        await _synchronizer.Received(1).SyncAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SendsSessionStartedNotification()
    {
        await CreateHandler().HandleAsync(
            new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", Now));

        await _notifications.Received(1).NotifyAsync(
            Arg.Is<string>(t => t.Contains("started")), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
