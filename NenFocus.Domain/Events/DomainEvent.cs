namespace NenFocus.Domain.Events;

public abstract class DomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; }

    protected DomainEvent(DateTime occurredAt)
    {
        OccurredAt = occurredAt;
    }
}