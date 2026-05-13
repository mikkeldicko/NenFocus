using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.ComputeActivePolicySlice;
// The Query (The "What")
public record ComputeActivePolicySliceQuery(DateTime At) : IQuery<ActivePolicySlice>;
// The Handler (The "How")
public sealed class ComputeActivePolicySliceQueryHandler : IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>
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
        // 1. Fetch active blocks and sessions
        var blocks = await _blocks.GetForRangeAsync(query.At.AddSeconds(-1), query.At.AddSeconds(1), ct);
        var sessions = await _sessions.GetActiveSessionsAsync(ct);

        // 2. Collect unique Profile IDs
        var profileIds = blocks.Select(b => b.BlockProfileId)
            .Concat(sessions.Select(s => s.BlockProfileId))
            .Where(id => id != Guid.Empty)
            .Distinct();

        // 3. Load the full profiles from the Domain
        var profiles = new List<NenFocus.Domain.Aggregates.BlockProfile>();
        foreach (var id in profileIds)
        {
            var profile = await _profiles.GetByIdAsync(id, ct);
            if (profile != null) profiles.Add(profile);
        }

        // 4. Delegate complex logic to the Deep Module (PolicyEngine)
        return _policyEngine.ComputeSlice(query.At, profiles, sessions, blocks);
    }
}