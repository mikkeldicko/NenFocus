using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

/// <summary>
/// Fires when a break starts during a session.
/// Sends a break notification and schedules the auto-resume reminder.
/// </summary>
public sealed class BreakStartedEventHandler
{
    private readonly INotificationService _notifications;

    public BreakStartedEventHandler(INotificationService notifications)
        => _notifications = notifications;

    public async Task HandleAsync(
        BreakStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await _notifications.NotifyAsync(
            title: "Break time",
            body: $"Break #{domainEvent.BreakNumber} — step away and recharge.",
            cancellationToken: cancellationToken);
    }
}
