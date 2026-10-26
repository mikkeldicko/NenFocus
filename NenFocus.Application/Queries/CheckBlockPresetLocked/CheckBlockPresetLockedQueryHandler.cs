using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.CheckBlockPresetLocked;

public sealed class CheckBlockPresetLockedQueryHandler : IQueryHandler<CheckBlockPresetLockedQuery, bool>
{
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;
    private readonly IBlockRepository _blocks;
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;

    public CheckBlockPresetLockedQueryHandler(
        IBlockPresetRepository presets,
        ISessionRepository sessions,
        IBlockRepository blocks,
        INenContractRepository contracts,
        ISystemClock clock)
    {
        _presets = presets;
        _sessions = sessions;
        _blocks = blocks;
        _contracts = contracts;
        _clock = clock;
    }

    public async Task<bool> HandleAsync(CheckBlockPresetLockedQuery query, CancellationToken ct = default)
    {
        var preset = await _presets.GetByIdAsync(query.PresetId, ct);
        if (preset is null) return false;

        // One "now" for the blocks and the deficits.
        var now = _clock.Now;
        var running = await _sessions.GetActiveAsync(ct);
        var blocksUsingIt = await _blocks.GetUsingPresetAsync(query.PresetId, now, ct);

        // Every open deficit of every contract in effect; the preset counts only its own.
        var openDeficits = new List<OutstandingDeficit>();
        foreach (var contract in await _contracts.GetAllInEffectAsync(ct))
            openDeficits.AddRange(contract.Deficits(await _blocks.GetForContractAsync(contract.Id, ct), now));

        return preset.IsLockedAt(now, running, blocksUsingIt, openDeficits);
    }
}
