using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects; // Added

namespace NenFocus.Application.EventHandlers;

public sealed class SessionStartedEventHandler
{
    private readonly IEnforcementService _enforcement;
    private readonly IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice> _policyQuery;
    private readonly INotificationService _notifications;

    public SessionStartedEventHandler(
        IEnforcementService enforcement,
        IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice> policyQuery,
        INotificationService notifications)
    {
        _enforcement = enforcement;
        _policyQuery = policyQuery;
        _notifications = notifications;
    }

    public async Task HandleAsync(
        SessionStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // FIX: Records use positional parameters: (DateTime.UtcNow)
        var policy = await _policyQuery.HandleAsync(
     new ComputeActivePolicySliceQuery(DateTime.Now), // Not UtcNow
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