using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class BreakStartedEventHandler : IDomainEventHandler<BreakStartedEvent>
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotificationService _notifications;
    private readonly ISystemClock _clock;

    public BreakStartedEventHandler(
        IEnforcementSynchronizer synchronizer,
        INotificationService notifications,
        ISystemClock clock)
    {
        _synchronizer = synchronizer;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task HandleAsync(
        BreakStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // PolicyEngine decides what the break lifts (only the on-break session's own preset,
        // and only if its contract allows pausing) — never "remove everything" (Gap 62).
        await _synchronizer.SyncAsync(_clock.UtcNow, cancellationToken);

        await _notifications.NotifyAsync(
            title: "Break time",
            body: $"Break #{domainEvent.BreakNumber} — step away and recharge.",
            cancellationToken: cancellationToken);
    }
}
