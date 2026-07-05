namespace NenFocus.Domain.Events;

public class NenContractActivatedEvent : DomainEvent
{
    public Guid ContractId { get; }

    public NenContractActivatedEvent(Guid contractId, DateTime occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
    }
}
