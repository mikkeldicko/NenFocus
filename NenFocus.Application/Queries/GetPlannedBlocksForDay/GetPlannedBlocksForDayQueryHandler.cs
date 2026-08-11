using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetPlannedBlocksForDay;

public sealed class GetPlannedBlocksForDayQueryHandler
    : IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;

    public GetPlannedBlocksForDayQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PlannedBlockDto>> HandleAsync(
        GetPlannedBlocksForDayQuery query,
        CancellationToken cancellationToken = default)
    {
        var blocks = await _blocks.GetForDayAsync(query.Date, cancellationToken);

        var allPresets = await _presets.GetAllAsync(cancellationToken);
        var presetMap = allPresets.ToDictionary(p => p.Id);
        var now = _clock.UtcNow;

        var contractIds = blocks.Select(b => b.ContractId).Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        var inEffectByContractId = new Dictionary<Guid, bool>();
        foreach (var contractId in contractIds)
        {
            var contract = await _contracts.GetByIdAsync(contractId, cancellationToken);
            inEffectByContractId[contractId] = contract?.IsInEffect ?? false;
        }

        return blocks.Select(b =>
        {
            var preset = presetMap.TryGetValue(b.BlockPresetId, out var p) ? p : null;
            return new PlannedBlockDto
            {
                Id = b.Id,
                PresetId = b.BlockPresetId,
                PresetName = preset?.Name ?? "Unknown",
                BlockPresetType = b is FocusBlock ? BlockPresetType.Focus : BlockPresetType.Background,
                Start = b.Start,
                End = b.End,
                WebsiteFilterType = preset?.WebsiteFilterType ?? WebsiteFilterType.Blocklist,
                ContractId = b.ContractId,
                IsCompleted = b is FocusBlock fb && fb.RitualCompletedAt.HasValue,
                HasEnded = b.HasEnded(now),
                IsUnderInEffectContract = b.ContractId.HasValue && inEffectByContractId.GetValueOrDefault(b.ContractId.Value)
            };
        }).ToList();
    }
}
