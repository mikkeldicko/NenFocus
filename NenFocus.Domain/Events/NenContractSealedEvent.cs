using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class NenContractSealedEvent : DomainEvent
{
    public Guid ContractId { get; }
    public string Name { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }

    public NenContractSealedEvent(Guid contractId, string name, DateOnly startDate, DateOnly endDate, Moment occurredAt)
        : base(occurredAt)
    {
        ContractId = contractId;
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
    }
}
