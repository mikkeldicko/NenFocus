using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;

namespace NenFocus.Infrastructure.Events;

/// <summary>
/// Resolves event handlers from a fresh DI scope per dispatch, keeping scoped
/// services (DbContext, repositories) isolated between events.
/// </summary>
public sealed class InProcessDomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public InProcessDomainEventPublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async Task PublishAsync(
        DomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await DispatchAsync(domainEvent, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PublishAllAsync(
        IEnumerable<DomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchAsync(domainEvent, cancellationToken);
        }
    }

    private async Task DispatchAsync(DomainEvent domainEvent, CancellationToken ct)
    {
        // Resolve a fresh scope so each event dispatch gets scoped services
        // (repositories, DbContext) without leaking state between events.
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        switch (domainEvent)
        {
            case SessionStartedEvent e:
                await sp.GetRequiredService<SessionStartedEventHandler>()
                        .HandleAsync(e, ct);
                break;

            case SessionEndedEvent e:
                await sp.GetRequiredService<SessionEndedEventHandler>()
                        .HandleAsync(e, ct);
                break;

            case BreakStartedEvent e:
                await sp.GetRequiredService<BreakStartedEventHandler>()
                        .HandleAsync(e, ct);
                break;

            case BlockProfileCreatedEvent e:
                await sp.GetRequiredService<BlockProfileCreatedEventHandler>()
                        .HandleAsync(e, ct);
                break;

            default:
                break;
        }
    }
}
