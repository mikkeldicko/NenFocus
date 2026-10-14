using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockAwaitingRitual;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Services;

/// <summary>
/// Singleton: remembers the last answer so Changed fires only when it changes. Written on the
/// heartbeat's thread and read on the UI thread, so the answer sits behind a lock (a Guid? is
/// too wide to read in one step).
/// </summary>
public sealed class RitualPrompt : IRitualPrompt
{
    private readonly IQueryDispatcher _queries;
    private readonly object _gate = new();
    private Guid? _pendingFocusBlockId;

    public RitualPrompt(IQueryDispatcher queries)
    {
        _queries = queries;
    }

    public Guid? PendingFocusBlockId
    {
        get { lock (_gate) return _pendingFocusBlockId; }
    }

    public event EventHandler<Guid?>? Changed;

    public async Task RefreshAsync(Moment at, CancellationToken cancellationToken = default)
    {
        var awaiting = await _queries.QueryAsync(new GetFocusBlockAwaitingRitualQuery(at), cancellationToken);

        lock (_gate)
        {
            if (_pendingFocusBlockId == awaiting) return;
            _pendingFocusBlockId = awaiting;
        }

        Changed?.Invoke(this, awaiting);
    }
}
