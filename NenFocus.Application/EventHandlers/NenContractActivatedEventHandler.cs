using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class NenContractActivatedEventHandler : IDomainEventHandler<NenContractActivatedEvent>
{
    private readonly INenContractRepository _contracts;
    private readonly INotifier _notifier;

    public NenContractActivatedEventHandler(
        INenContractRepository contracts,
        INotifier notifier)
    {
        _contracts = contracts;
        _notifier = notifier;
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

        await _notifier.NotifyAsync(new ContractActivated(contract.Name), cancellationToken);
    }
}
