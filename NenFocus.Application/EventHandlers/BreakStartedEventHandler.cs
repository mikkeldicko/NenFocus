using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class BreakStartedEventHandler
{
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;

    public BreakStartedEventHandler(
        IEnforcementService enforcement,
        INotificationService notifications)
    {
        _enforcement = enforcement;
        _notifications = notifications;
    }

    public async Task HandleAsync(
        BreakStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await _enforcement.RemoveEnforcementAsync(cancellationToken);

        await _notifications.NotifyAsync(
            title: "Break time",
            body: $"Break #{domainEvent.BreakNumber} — step away and recharge.",
            cancellationToken: cancellationToken);
    }
}
