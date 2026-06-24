using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionEndedEventHandler
{
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;
    private readonly ISessionStateService _sessionState;

    public SessionEndedEventHandler(
        IEnforcementService enforcement,
        INotificationService notifications,
        ISessionStateService sessionState)
    {
        _enforcement = enforcement;
        _notifications = notifications;
        _sessionState = sessionState;
    }

    public async Task HandleAsync(
        SessionEndedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // Clear session state only after the DB commit has completed (called from
        // EfCoreUnitOfWork.CommitAsync after SaveChangesAsync). This prevents the
        // ScheduledEnforcementService from seeing a null ActiveSession while the DB
        // still shows the session as Active, which would falsely re-trigger the Ritual.
        _sessionState.Clear();

        await _enforcement.RemoveEnforcementAsync(cancellationToken);
        await _notifications.CancelAllRemindersAsync(cancellationToken);
        await _notifications.NotifyAsync(
            title: "Session complete",
            body: "Great work. Your focus session has ended.",
            cancellationToken: cancellationToken);
    }
}
