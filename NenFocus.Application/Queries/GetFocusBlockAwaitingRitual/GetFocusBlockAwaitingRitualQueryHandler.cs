using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetFocusBlockAwaitingRitual;

public sealed class GetFocusBlockAwaitingRitualQueryHandler
    : IQueryHandler<GetFocusBlockAwaitingRitualQuery, Guid?>
{
    private readonly IBlockRepository _blocks;
    private readonly ISessionRepository _sessions;

    public GetFocusBlockAwaitingRitualQueryHandler(
        IBlockRepository blocks,
        ISessionRepository sessions)
    {
        _blocks = blocks;
        _sessions = sessions;
    }

    public async Task<Guid?> HandleAsync(
        GetFocusBlockAwaitingRitualQuery query,
        CancellationToken ct = default)
    {
        // Same ±1s buffered fetch as ComputeActivePolicySliceQueryHandler (SQLite datetime
        // slop); FocusBlock.IsAwaitingRitual is the precise authority on what qualifies.
        var blocks = await _blocks.GetForRangeAsync(new TimeSlot(query.At - TimeSpan.FromSeconds(1), query.At + TimeSpan.FromSeconds(1)), ct);

        var awaiting = blocks.OfType<FocusBlock>().FirstOrDefault(b => b.IsAwaitingRitual(query.At));
        if (awaiting is null)
            return null;

        // Read from the database, not ISessionStateService's in-memory copy, so both halves
        // of the answer come from the same source and can't disagree.
        if (await _sessions.GetActiveAsync(ct) is not null)
            return null;

        return awaiting.Id;
    }
}
