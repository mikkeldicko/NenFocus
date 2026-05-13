using NenFocus.Domain.ValueObjects; // Updated reference

namespace NenFocus.Application.Interfaces;

public interface IEnforcementService
{
    // Updated to use ActivePolicySlice
    Task ApplyPolicyAsync(ActivePolicySlice policy, CancellationToken cancellationToken = default);

    Task RemoveEnforcementAsync(CancellationToken cancellationToken = default);

    // Updated return type
    Task<ActivePolicySlice?> GetCurrentPolicyAsync(CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(CancellationToken cancellationToken = default);
}