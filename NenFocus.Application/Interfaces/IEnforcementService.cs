using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

public interface IEnforcementService
{
    Task ApplyPolicyAsync(ActivePolicySlice policy, CancellationToken cancellationToken = default);

    Task RemoveEnforcementAsync(CancellationToken cancellationToken = default);

    Task<ActivePolicySlice?> GetCurrentPolicyAsync(CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(CancellationToken cancellationToken = default);
}
