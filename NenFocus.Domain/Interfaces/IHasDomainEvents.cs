using NenFocus.Domain.Events;

namespace NenFocus.Domain.Interfaces;

public interface IHasDomainEvents
{
    IReadOnlyList<DomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
