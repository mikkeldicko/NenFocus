using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// The app's view of the running session — none or one. Read from the database through
/// GetActiveSessionQuery, never pushed in, so it can't be handed a wrong or partial copy.
/// Refreshed at startup and by the session started/ended event handlers (Gap 92, A3).
/// </summary>
public interface IActiveSessionState
{
    SessionDto? ActiveSession { get; }

    event EventHandler<SessionDto?>? SessionChanged;

    /// <summary>Re-reads the running session and raises <see cref="SessionChanged"/> with it.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
