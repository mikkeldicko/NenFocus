using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NenFocus.Infrastructure.Persistence;

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

        if (scope.ServiceProvider.GetService(handlerType) is not IDomainEventHandler handler)
            return;

        await handler.HandleAsync(domainEvent, ct);
        RefuseUnsavedChanges(scope.ServiceProvider, handler);
    }

    // Events are published after the commit, and nothing saves this scope — so a change a handler
    // makes here would vanish silently. A reaction that changes state dispatches a command, which
    // saves in its own transaction (Gap 110).
    private static void RefuseUnsavedChanges(IServiceProvider scope, IDomainEventHandler handler)
    {
        if (scope.GetService<NenFocusDbContext>() is not { } context || !context.ChangeTracker.HasChanges())
            return;

        var changed = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.GetType().Name)
            .Distinct();
        throw new InvalidOperationException(
            $"{handler.GetType().Name} changed {string.Join(", ", changed)}, which nothing would save — " +
            "an event handler only reads; a reaction that changes state must dispatch a command.");
    }
}
