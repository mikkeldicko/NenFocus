using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// The blocked attempts, as a collection (Gap 110): append-only history, so no Remove (Design
/// Review Rule 17). Replaces IBlockedAttemptTracker, which saved by itself and served reads (Gap 112, I3).
/// </summary>
public interface IBlockedAttemptRepository
{
    void Add(BlockedAttempt attempt);

    /// <summary>The latest attempt at <paramref name="resourceName"/> in this session — narrows; BlockedAttempt.IsRepeatOf decides.</summary>
    Task<BlockedAttempt?> GetLatestAsync(Guid sessionId, string resourceName, CancellationToken ct = default);

    /// <summary>Every attempt in this session, newest first.</summary>
    Task<IReadOnlyList<BlockedAttempt>> GetForSessionAsync(Guid sessionId, CancellationToken ct = default);
}
