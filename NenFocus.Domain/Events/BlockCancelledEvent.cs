using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class PlannedBlockCancelledEvent : DomainEvent
{
    public Guid PlannedBlockId { get; }

    public PlannedBlockCancelledEvent(Guid plannedBlockId, Moment occurredAt)
        : base(occurredAt)
    {
        PlannedBlockId = plannedBlockId;
    }
}
