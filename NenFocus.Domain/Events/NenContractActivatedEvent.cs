using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class NenContractActivatedEvent : DomainEvent
{
    public Guid ContractId { get; }

    public NenContractActivatedEvent(Guid contractId, Moment occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
    }
}
