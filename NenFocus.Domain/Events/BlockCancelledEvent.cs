using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class BlockCancelledEvent : DomainEvent
{
    public Guid BlockId { get; }

    public BlockCancelledEvent(Guid blockId, Moment occurredAt)
        : base(occurredAt)
    {
        BlockId = blockId;
    }
}
