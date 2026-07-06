using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class BlockPresetCreatedEventHandler : IDomainEventHandler<BlockPresetCreatedEvent>
{
    public Task HandleAsync(
        BlockPresetCreatedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
