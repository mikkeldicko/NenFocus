using NenFocus.Domain.Events;

namespace NenFocus.Application.Interfaces;

public interface IDomainEventPublisher
{

    Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);
    Task PublishAllAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
