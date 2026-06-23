using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionEndedEventHandler
{
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;

    public SessionEndedEventHandler(
        IEnforcementService enforcement,
        INotificationService notifications)
    {
        _enforcement = enforcement;
        _notifications = notifications;
    }

    public async Task HandleAsync(
        SessionEndedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await _enforcement.RemoveEnforcementAsync(cancellationToken);
        await _notifications.CancelAllRemindersAsync(cancellationToken);
        await _notifications.NotifyAsync(
            title: "Session complete",
            body: "Great work. Your focus session has ended.",
            cancellationToken: cancellationToken);
    }
}
