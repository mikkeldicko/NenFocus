using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Makes the OS's actual blocking match what PolicyEngine says should be blocked at the
/// given instant. Safe to call from anywhere, any time, concurrently — calls are
/// serialized, so a stale answer can never overwrite a fresh one. The only place that
/// turns a computed ActivePolicySlice into an IEnforcementService apply/remove call.
/// </summary>
public interface IEnforcementSynchronizer
{
    /// <returns>The slice that was enforced, so a caller can read what is blocked without
    /// computing a second, possibly different, answer.</returns>
    Task<ActivePolicySlice> SyncAsync(Moment at, CancellationToken ct = default);
}
