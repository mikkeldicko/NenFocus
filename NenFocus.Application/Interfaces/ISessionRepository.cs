using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

public interface ISessionRepository
{
    Task<Session?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sessions that started inside <paramref name="startedWithin"/>, newest first. For "these
    /// days", the caller asks <c>UserTimeZone.SlotCovering</c> — the repository only knows moments.
    /// </summary>
    Task<IReadOnlyList<Session>> GetHistoryAsync(
        TimeSlot startedWithin,
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

    /// <summary>
    /// Returns every Completed or Missed session whose post-session screen has
    /// never been dismissed (PostSessionAcknowledgedAt is null), ordered by
    /// EndedAt ascending — oldest first, so pending post-sessions can be replayed
    /// to the user in the order they actually happened.
    /// </summary>
    Task<IReadOnlyList<Session>> GetUnacknowledgedPostSessionsAsync(CancellationToken ct = default);
}
