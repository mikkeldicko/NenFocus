using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class NenContractActivatedEventHandler : IDomainEventHandler<NenContractActivatedEvent>
{
    private readonly INenContractRepository _contracts;
    private readonly INotificationService _notifications;

    public NenContractActivatedEventHandler(
        INenContractRepository contracts,
        INotificationService notifications)
    {
        _contracts = contracts;
        _notifications = notifications;
    }

    public async Task HandleAsync(
        NenContractActivatedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        var contract = await _contracts.GetByIdAsync(domainEvent.ContractId, cancellationToken);
        if (contract is null)
        {
            return;
        }

        await _notifications.NotifyAsync(
            title: "Contract started",
            body: $"Your contract \"{contract.Name}\" is now active.",
            cancellationToken: cancellationToken);
    }
}
