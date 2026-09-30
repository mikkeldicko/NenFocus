using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class NenContractBrokenEvent : DomainEvent
{
    public Guid ContractId { get; }

    public NenContractBrokenEvent(Guid contractId, Moment occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
    }
}
