using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Interfaces;

public interface ISessionRepository
{
    Task<Session?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetHistoryAsync(
        DateOnly from,
        DateOnly to,
        Guid? presetId = null,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Session session, CancellationToken cancellationToken = default);

    Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns true if any session (of any status) already references the given
    /// Focus block as its origin. Used to make missed-session creation idempotent.
    /// </summary>
    Task<bool> ExistsForOriginatingBlockAsync(Guid focusBlockId, CancellationToken ct = default);

    /// <summary>
    /// Returns the session (of any status) that references the given Focus block as
    /// its origin, or null if none exists yet. Used to find a resolved block's actual
    /// elapsed time for deficit and completed-hours computation.
    /// </summary>
    Task<Session?> GetByOriginatingBlockIdAsync(Guid focusBlockId, CancellationToken ct = default);
}
