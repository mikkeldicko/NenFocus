using NenFocus.Domain.Events;

namespace NenFocus.Application.Interfaces;

public interface IDomainEventHandler
{
    Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);
}

public interface IDomainEventHandler<TEvent> : IDomainEventHandler
    where TEvent : DomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);

    Task IDomainEventHandler.HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
        => HandleAsync((TEvent)domainEvent, cancellationToken);
}
