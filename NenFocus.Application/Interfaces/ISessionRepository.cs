using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

public interface ISessionRepository
{
    /// <summary>The running session, or null. At most one runs (Gap 92: the Domain's "Focus time never
    /// overlaps", backed by a unique index) — "is one running" is <c>is not null</c>.</summary>
    Task<Session?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sessions that started inside <paramref name="startedWithin"/>, newest first. For "these
    /// days", the caller asks <c>UserTimeZone.SlotCovering</c> — the repository only knows moments.
    /// A session's preset is its block's (Gap 114, D1), so no preset filter is offered here.
    /// </summary>
    Task<IReadOnlyList<Session>> GetHistoryAsync(
        TimeSlot startedWithin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the session (of any status) that references the given Focus block as
    /// its origin, or null if none exists yet. At most one exists (unique index, Gap 92) — "already
    /// has one" is <c>is not null</c>. Used to find a resolved block's actual
    /// elapsed time for deficit and completed-hours computation.
    /// </summary>
    Task<Session?> GetByOriginatingBlockIdAsync(Guid focusBlockId, CancellationToken ct = default);

    /// <summary>
    /// Returns every Completed session whose post-session screen has never been dismissed
    /// (Reflection.AcknowledgedAt is null), ordered by EndedAt ascending — oldest first, so
    /// pending post-sessions can be replayed to the user in the order they happened.
    /// </summary>
    Task<IReadOnlyList<Session>> GetUnacknowledgedPostSessionsAsync(CancellationToken ct = default);

    /// <summary>Adds a new session; it is saved when the command commits (Gap 110).</summary>
    void Add(Session session);
}
