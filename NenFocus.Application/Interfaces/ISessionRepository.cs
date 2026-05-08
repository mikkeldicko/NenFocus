using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Repository contract for Session aggregate persistence.
/// Implemented by NenFocus.Infrastructure.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Returns the currently active session, or null if none is running.
    /// </summary>
    Task<Session?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a session by its unique identifier.
    /// </summary>
    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all sessions within the given date range, optionally filtered by profile.
    /// </summary>
    Task<IReadOnlyList<Session>> GetHistoryAsync(
        DateOnly from,
        DateOnly to,
        Guid? profileId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new or updated session (upsert by Id).
    /// </summary>
    Task SaveAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if any session is currently in an active state.
    /// Used by StartSessionCommand to enforce the single-session rule.
    /// </summary>
    Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NenFocus.Domain.Entities.Session>> GetActiveSessionsAsync(CancellationToken ct = default);
}
