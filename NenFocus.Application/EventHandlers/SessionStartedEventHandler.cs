using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionStartedEventHandler
{
    private readonly INotificationService _notifications;

    public SessionStartedEventHandler(INotificationService notifications)
    {
        _notifications = notifications;
    }

    public async Task HandleAsync(
        SessionStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await _notifications.NotifyAsync(
            title: "Nen session started",
            body: $"Focus session is now active. Stay locked in.",
            cancellationToken: cancellationToken);
    }
}
