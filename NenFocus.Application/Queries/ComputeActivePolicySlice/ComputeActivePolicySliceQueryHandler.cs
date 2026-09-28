using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeActivePolicySlice;

public sealed class ComputeActivePolicySliceQueryHandler
    : IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;
    private readonly IPolicyEngine _policyEngine;

    public ComputeActivePolicySliceQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISessionRepository sessions,
        IPolicyEngine policyEngine)
    {
        _blocks = blocks;
        _presets = presets;
        _sessions = sessions;
        _policyEngine = policyEngine;
    }

    public async Task<ActivePolicySlice> HandleAsync(ComputeActivePolicySliceQuery query, CancellationToken ct)
    {
        // Loose fetch; PolicyEngine asks each block IsActive, which already rules out cancelled ones.
        var blocks = await _blocks.GetForRangeAsync(query.At.AddSeconds(-1), query.At.AddSeconds(1), ct);
        var sessions = await _sessions.GetActiveSessionsAsync(ct);

        var presetIds = blocks.Select(b => b.BlockPresetId)
            .Concat(sessions.Select(s => s.BlockPresetId))
            .Where(id => id != Guid.Empty)
            .Distinct();

        var presets = new List<BlockPreset>();
        foreach (var id in presetIds)
        {
            var preset = await _presets.GetByIdAsync(id, ct);
            if (preset != null) presets.Add(preset);
        }

        return _policyEngine.ComputeSlice(query.At, presets, sessions, blocks);
    }
}
