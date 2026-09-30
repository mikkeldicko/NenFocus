using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class NenContractCompletedEvent : DomainEvent
{
    public Guid ContractId { get; }

    public NenContractCompletedEvent(Guid contractId, Moment occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
    }
}
