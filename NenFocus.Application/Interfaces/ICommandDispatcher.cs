namespace NenFocus.Application.Interfaces;

/// <summary>
/// Dispatches a command to its registered ICommandHandler (transaction + validation
/// decorators included, unchanged), resolved from a fresh DI scope created just for this
/// call — never a DbContext shared across unrelated operations. The narrow entry point
/// every ViewModel should use instead of holding ICommandHandler instances directly, so
/// no caller can accidentally recreate the shared-DbContext concurrency hazard a
/// directly-injected handler produced (DESIGN_GAP_HISTORY.md).
/// </summary>
public interface ICommandDispatcher
{
    Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}
