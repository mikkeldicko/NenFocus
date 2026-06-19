using NenFocus.Application.Interfaces;
using NenFocus.Domain.Interfaces;

namespace NenFocus.Infrastructure.Persistence;

public sealed class EfCoreUnitOfWork : IUnitOfWork
{
    private readonly NenFocusDbContext _context;
    private readonly IDomainEventPublisher _publisher;

    public EfCoreUnitOfWork(NenFocusDbContext context, IDomainEventPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        var aggregates = _context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        await _context.SaveChangesAsync(ct);

        foreach (var aggregate in aggregates)
        {
            await _publisher.PublishAllAsync(aggregate.DomainEvents, ct);
            aggregate.ClearDomainEvents();
        }
    }
}
