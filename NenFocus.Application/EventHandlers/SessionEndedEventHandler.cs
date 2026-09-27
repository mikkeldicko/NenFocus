using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionEndedEventHandler : IDomainEventHandler<SessionEndedEvent>
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotificationService _notifications;
    private readonly ISessionStateService _sessionState;
    private readonly ISystemClock _clock;

    public SessionEndedEventHandler(
        IEnforcementSynchronizer synchronizer,
        INotificationService notifications,
        ISessionStateService sessionState,
        ISystemClock clock)
    {
        _synchronizer = synchronizer;
        _notifications = notifications;
        _sessionState = sessionState;
        _clock = clock;
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

        // Sync, never "remove everything" — a Background Block still running must stay
        // enforced after the session ends (Gap 62).
        await _synchronizer.SyncAsync(_clock.UtcNow, cancellationToken);
        await _notifications.CancelAllRemindersAsync(cancellationToken);
        await _notifications.NotifyAsync(
            title: "Session complete",
            body: "Great work. Your focus session has ended.",
            cancellationToken: cancellationToken);
    }
}
