using NenFocus.Domain.Events;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Contract for dispatching domain events to their registered handlers.
/// Wires domain events (raised inside aggregates) to Application-layer event handlers.
/// Implemented by NenFocus.Infrastructure using an in-process dispatcher.
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>
    /// Publishes a single domain event to all registered handlers.
    /// Handlers are resolved from DI and invoked sequentially.
    /// </summary>
    Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes all domain events collected on an aggregate after a command completes.
    /// Call this at the end of each command handler, passing the aggregate's DomainEvents.
    /// Clears the event collection on the aggregate after dispatching.
    /// </summary>
    Task PublishAllAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
