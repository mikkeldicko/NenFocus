using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class BlockProfileCreatedEventHandler
{
    public Task HandleAsync(
        BlockProfileCreatedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
