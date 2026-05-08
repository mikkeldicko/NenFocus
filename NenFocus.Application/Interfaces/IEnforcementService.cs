using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Contract for the OS-level blocking enforcement engine.
/// Implemented by NenFocus.Infrastructure using platform-specific APIs
/// (process monitoring on Windows/macOS, hosts file / DNS for websites).
/// The Application layer tells it WHAT to block; Infrastructure decides HOW.
/// </summary>
public interface IEnforcementService
{
    /// <summary>
    /// Applies a policy slice to the OS enforcement layer.
    /// Atomically replaces any currently active policy.
    /// Called by SessionStartedEventHandler and on planned block boundary transitions.
    /// </summary>
    Task ApplyPolicyAsync(PolicySliceDto policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all active enforcement rules.
    /// Called by SessionEndedEventHandler when the session completes.
    /// </summary>
    Task RemoveEnforcementAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the currently enforced policy, or null if no enforcement is active.
    /// Used to verify enforcement state after applying a policy.
    /// </summary>
    Task<PolicySliceDto?> GetCurrentPolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the enforcement engine is currently active and running.
    /// Infrastructure implementations should check the underlying OS service/daemon.
    /// </summary>
    Task<bool> IsActiveAsync(CancellationToken cancellationToken = default);

}
