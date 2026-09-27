using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class BreakEndedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 30, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public BreakEndedEventHandlerTests() => _clock.UtcNow.Returns(Now);

    private BreakEndedEventHandler CreateHandler() => new(_synchronizer, _notifications, _clock);

    [Fact]
    public async Task HandleAsync_SyncsEnforcementAtCurrentTime()
    {
        // Gap 62: previously nothing reacted to a break ending, so blocking only came back
        // on the next heartbeat tick (up to 5s late).
        await CreateHandler().HandleAsync(new BreakEndedEvent(Guid.NewGuid(), breakNumber: 1, Now));

        await _synchronizer.Received(1).SyncAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SendsBreakOverNotification()
    {
        await CreateHandler().HandleAsync(new BreakEndedEvent(Guid.NewGuid(), breakNumber: 1, Now));

        await _notifications.Received(1).NotifyAsync(
            "Break over", "Back to focus.", Arg.Any<CancellationToken>());
    }
}
