using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetActiveSession;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Singleton: holds the running session for the app's lifetime and raises SessionChanged.
/// Reads through the query dispatcher, which gives each read its own scope, so no
/// DbContext is pinned for the app's run.
/// </summary>
public sealed class ActiveSessionState : IActiveSessionState
{
    private readonly IQueryDispatcher _queries;

    public ActiveSessionState(IQueryDispatcher queries)
    {
        _queries = queries;
    }

    public SessionDto? ActiveSession { get; private set; }

    public event EventHandler<SessionDto?>? SessionChanged;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ActiveSession = await _queries.QueryAsync(new GetActiveSessionQuery(), cancellationToken);
        SessionChanged?.Invoke(this, ActiveSession);
    }
}
