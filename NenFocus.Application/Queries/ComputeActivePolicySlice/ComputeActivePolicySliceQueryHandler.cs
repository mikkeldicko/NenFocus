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
    private readonly INenContractRepository _contracts;
    private readonly IPolicyEngine _policyEngine;

    public ComputeActivePolicySliceQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISessionRepository sessions,
        INenContractRepository contracts,
        IPolicyEngine policyEngine)
    {
        _blocks = blocks;
        _presets = presets;
        _sessions = sessions;
        _contracts = contracts;
        _policyEngine = policyEngine;
    }

    public async Task<ActivePolicySlice> HandleAsync(ComputeActivePolicySliceQuery query, CancellationToken ct)
    {
        var allBlocks = await _blocks.GetForRangeAsync(query.At.AddSeconds(-1), query.At.AddSeconds(1), ct);
        var blocks = allBlocks.Where(b => !b.CancelledAt.HasValue).ToList();
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

        // Only resolved when a break is actually in progress against a committed
        // block — this is a rare path, so fetching one contract at a time here (rather
        // than a bulk query) mirrors the same "resolve only what's referenced" style
        // already used for presets above.
        var contractIds = blocks.OfType<FocusBlock>()
            .Where(b => b.ContractId.HasValue)
            .Select(b => b.ContractId!.Value)
            .Distinct();

        var contracts = new List<NenContract>();
        foreach (var id in contractIds)
        {
            var contract = await _contracts.GetByIdAsync(id, ct);
            if (contract != null) contracts.Add(contract);
        }

        return _policyEngine.ComputeSlice(query.At, presets, sessions, blocks, contracts);
    }
}
