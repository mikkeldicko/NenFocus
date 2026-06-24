using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class BlockProfileCreatedEventHandler : IDomainEventHandler<BlockProfileCreatedEvent>
{
    public Task HandleAsync(
        BlockProfileCreatedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
