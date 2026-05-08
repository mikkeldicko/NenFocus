using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

/// <summary>
/// Fires when a session ends.
/// Removes OS enforcement, cancels reminders, and notifies the user.
/// </summary>
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
        // Remove all OS-level blocking rules
        await _enforcement.RemoveEnforcementAsync(cancellationToken);

        // Cancel any pending break reminders
        await _notifications.CancelAllRemindersAsync(cancellationToken);

        // Notify the user the session is complete
        await _notifications.NotifyAsync(
            title: "Session complete",
            body: "Great work. Your focus session has ended.",
            cancellationToken: cancellationToken);
    }
}
