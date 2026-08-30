namespace NenFocus.Application.Interfaces;

/// <summary>
/// Dispatches a query to its registered IQueryHandler, resolved from a fresh DI scope
/// created just for this call — never a DbContext shared across unrelated operations.
/// The narrow entry point every ViewModel should use instead of holding IQueryHandler
/// instances directly, so no caller can accidentally recreate the shared-DbContext
/// concurrency hazard a directly-injected handler produced (DESIGN_GAP_HISTORY.md).
/// </summary>
public interface IQueryDispatcher
{
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
