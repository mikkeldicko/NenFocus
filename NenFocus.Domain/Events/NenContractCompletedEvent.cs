namespace NenFocus.Domain.Events;

public class NenContractCompletedEvent : DomainEvent
{
    public Guid ContractId { get; }

    public NenContractCompletedEvent(Guid contractId, DateTime occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
    }
}
