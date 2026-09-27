using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class BreakStartedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 25, 0, DateTimeKind.Utc);

    private readonly IEnforcementSynchronizer _synchronizer = Substitute.For<IEnforcementSynchronizer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public BreakStartedEventHandlerTests() => _clock.UtcNow.Returns(Now);

    private BreakStartedEventHandler CreateHandler() => new(_synchronizer, _notifications, _clock);

    [Fact]
    public async Task HandleAsync_SyncsEnforcementAtCurrentTime()
    {
        // Gap 62: previously removed ALL enforcement, unblocking co-occurring Background
        // Blocks and contracts that forbid pausing until the next heartbeat. PolicyEngine
        // now decides what the break lifts; the handler only asks for a sync.
        await CreateHandler().HandleAsync(new BreakStartedEvent(Guid.NewGuid(), breakNumber: 2, Now));

        await _synchronizer.Received(1).SyncAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NotifiesWithBreakNumber()
    {
        await CreateHandler().HandleAsync(new BreakStartedEvent(Guid.NewGuid(), breakNumber: 2, Now));

        await _notifications.Received(1).NotifyAsync(
            Arg.Any<string>(), Arg.Is<string>(body => body.Contains("Break #2")), Arg.Any<CancellationToken>());
    }
}
