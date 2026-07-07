namespace NenFocus.Domain.Events;

public class NenContractExtendedEvent : DomainEvent
{
    public Guid ContractId { get; }
    public DateOnly OldEndDate { get; }
    public DateOnly NewEndDate { get; }

    public NenContractExtendedEvent(Guid contractId, DateOnly oldEndDate, DateOnly newEndDate, DateTime occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
        OldEndDate = oldEndDate;
        NewEndDate = newEndDate;
    }
}
