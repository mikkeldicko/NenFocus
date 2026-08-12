using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class NenContractCompletedEventHandler : IDomainEventHandler<NenContractCompletedEvent>
{
    private readonly INenContractRepository _contracts;
    private readonly INotificationService _notifications;

    public NenContractCompletedEventHandler(
        INenContractRepository contracts,
        INotificationService notifications)
    {
        _contracts = contracts;
        _notifications = notifications;
    }

    public async Task HandleAsync(
        NenContractCompletedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        var contract = await _contracts.GetByIdAsync(domainEvent.ContractId, cancellationToken);
        if (contract is null)
        {
            return;
        }

        await _notifications.NotifyAsync(
            title: "Contract complete",
            body: $"Your contract \"{contract.Name}\" has ended.",
            cancellationToken: cancellationToken);
    }
}
