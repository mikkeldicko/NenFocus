using NenFocus.Domain.ValueObjects;
namespace NenFocus.Domain.Events;

public abstract class DomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public Moment OccurredAt { get; }

    protected DomainEvent(Moment occurredAt)
    {
        OccurredAt = occurredAt;
    }
}