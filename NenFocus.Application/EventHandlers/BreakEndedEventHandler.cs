using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class BreakEndedEventHandler : IDomainEventHandler<BreakEndedEvent>
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotificationService _notifications;
    private readonly ISystemClock _clock;

    public BreakEndedEventHandler(
        IEnforcementSynchronizer synchronizer,
        INotificationService notifications,
        ISystemClock clock)
    {
        _synchronizer = synchronizer;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task HandleAsync(
        BreakEndedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // Re-blocks the session's preset the moment the break ends, rather than on the
        // next heartbeat tick (Gap 62).
        await _synchronizer.SyncAsync(_clock.UtcNow, cancellationToken);

        await _notifications.NotifyAsync(
            title: "Break over",
            body: "Back to focus.",
            cancellationToken: cancellationToken);
    }
}
