namespace NenFocus.Domain.Events;

public class NenContractBrokenEvent : DomainEvent
{
    public Guid ContractId { get; }

    public NenContractBrokenEvent(Guid contractId, DateTime occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
    }
}
