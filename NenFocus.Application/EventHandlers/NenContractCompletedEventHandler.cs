using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class NenContractCompletedEventHandler : IDomainEventHandler<NenContractCompletedEvent>
{
    private readonly INenContractRepository _contracts;
    private readonly INotifier _notifier;

    public NenContractCompletedEventHandler(
        INenContractRepository contracts,
        INotifier notifier)
    {
        _contracts = contracts;
        _notifier = notifier;
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

        await _notifier.NotifyAsync(new ContractCompleted(contract.Name), cancellationToken);
    }
}
