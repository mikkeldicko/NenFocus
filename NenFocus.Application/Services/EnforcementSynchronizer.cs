using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Services;

/// <summary>
/// Application service: asks PolicyEngine (via ComputeActivePolicySliceQuery) what should be
/// blocked, then makes IEnforcementService match — no rule of its own. Registered as a
/// singleton so the lock is shared by every caller (heartbeat and event handlers alike);
/// IQueryDispatcher gives each computation its own fresh DI scope, so the singleton never
/// holds a DbContext.
/// </summary>
public sealed class EnforcementSynchronizer : IEnforcementSynchronizer
{
    private readonly IQueryDispatcher _queries;
    private readonly IEnforcementService _enforcement;

    // Compute + apply is one unit: without it, a caller that computed before a state change
    // could apply its stale slice after a caller that computed afterwards.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EnforcementSynchronizer(IQueryDispatcher queries, IEnforcementService enforcement)
    {
        _queries = queries;
        _enforcement = enforcement;
    }

    public async Task<ActivePolicySlice> SyncAsync(DateTime at, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var slice = await _queries.QueryAsync(new ComputeActivePolicySliceQuery(at), ct);

            if (slice.IsEmpty)
                await _enforcement.RemoveEnforcementAsync(ct);
            else
                await _enforcement.ApplyPolicyAsync(slice, ct);

            return slice;
        }
        finally
        {
            _lock.Release();
        }
    }
}
