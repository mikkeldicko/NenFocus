using NenFocus.Application.Interfaces;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeActivePolicySlice;

public sealed class ComputeActivePolicySliceQueryHandler
    : IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;
    private readonly ISessionRepository _sessions;
    private readonly IPolicyEngine _policyEngine;

    public ComputeActivePolicySliceQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles,
        ISessionRepository sessions,
        IPolicyEngine policyEngine)
    {
        _blocks = blocks;
        _profiles = profiles;
        _sessions = sessions;
        _policyEngine = policyEngine;
    }

    public async Task<ActivePolicySlice> HandleAsync(ComputeActivePolicySliceQuery query, CancellationToken ct)
    {
        var blocks = await _blocks.GetForRangeAsync(query.At.AddSeconds(-1), query.At.AddSeconds(1), ct);
        var sessions = await _sessions.GetActiveSessionsAsync(ct);

        var profileIds = blocks.Select(b => b.BlockProfileId)
            .Concat(sessions.Select(s => s.BlockProfileId))
            .Where(id => id != Guid.Empty)
            .Distinct();

        var profiles = new List<NenFocus.Domain.Aggregates.BlockProfile>();
        foreach (var id in profileIds)
        {
            var profile = await _profiles.GetByIdAsync(id, ct);
            if (profile != null) profiles.Add(profile);
        }

        return _policyEngine.ComputeSlice(query.At, profiles, sessions, blocks);
    }
}
