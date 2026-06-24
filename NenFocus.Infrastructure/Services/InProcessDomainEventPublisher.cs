using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Infrastructure.Services;

public sealed class InProcessDomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public InProcessDomainEventPublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
        => DispatchAsync(domainEvent, cancellationToken);

    public async Task PublishAllAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchAsync(domainEvent, cancellationToken);
        }
    }

    private async Task DispatchAsync(DomainEvent domainEvent, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());

        if (scope.ServiceProvider.GetService(handlerType) is IDomainEventHandler handler)
            await handler.HandleAsync(domainEvent, ct);
    }
}
