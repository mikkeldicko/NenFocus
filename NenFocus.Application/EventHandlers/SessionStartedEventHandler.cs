using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionStartedEventHandler : IDomainEventHandler<SessionStartedEvent>
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotificationService _notifications;
    private readonly ISystemClock _clock;

    public SessionStartedEventHandler(
        IEnforcementSynchronizer synchronizer,
        INotificationService notifications,
        ISystemClock clock)
    {
        _synchronizer = synchronizer;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task HandleAsync(
        SessionStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await _synchronizer.SyncAsync(_clock.UtcNow, cancellationToken);

        await _notifications.NotifyAsync(
            title: "Nen session started",
            body: "Focus session is now active. Stay locked in.",
            cancellationToken: cancellationToken);
    }
}
