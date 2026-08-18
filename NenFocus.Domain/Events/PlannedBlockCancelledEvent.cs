namespace NenFocus.Domain.Events;

public class PlannedBlockCancelledEvent : DomainEvent
{
    public Guid PlannedBlockId { get; }

    public PlannedBlockCancelledEvent(Guid plannedBlockId, DateTime occurredAt)
        : base(occurredAt)
    {
        PlannedBlockId = plannedBlockId;
    }
}
