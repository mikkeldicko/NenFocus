using NenFocus.Domain.Events;

namespace NenFocus.Domain.Entities;

public abstract class AggregateRoot
{
    private readonly List<DomainEvent> _domainEvents = [];

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void Raise(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}