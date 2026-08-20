using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionStartedEventHandler : IDomainEventHandler<SessionStartedEvent>
{
    private readonly IEnforcementService _enforcement;
    private readonly IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice> _policyQuery;
    private readonly INotificationService _notifications;
    private readonly ISystemClock _clock;

    public SessionStartedEventHandler(
        IEnforcementService enforcement,
        IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice> policyQuery,
        INotificationService notifications,
        ISystemClock clock)
    {
        _enforcement = enforcement;
        _policyQuery = policyQuery;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task HandleAsync(
        SessionStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyQuery.HandleAsync(
            new ComputeActivePolicySliceQuery(_clock.UtcNow),
            cancellationToken);

        if (policy is not null)
        {
            await _enforcement.ApplyPolicyAsync(policy, cancellationToken);
        }

        await _notifications.NotifyAsync(
            title: "Nen session started",
            body: "Focus session is now active. Stay locked in.",
            cancellationToken: cancellationToken);
    }
}