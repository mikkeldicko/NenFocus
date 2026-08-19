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
        var blocks = (await _blocks.GetForDayAsync(query.Date, cancellationToken))
            .Where(b => !b.CancelledAt.HasValue)
            .ToList();

        var allPresets = await _presets.GetAllAsync(cancellationToken);
        var presetMap = allPresets.ToDictionary(p => p.Id);
        var now = _clock.UtcNow;

        var contractIds = blocks.Select(b => b.ContractId).Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        var contractsById = new Dictionary<Guid, NenContract?>();
        foreach (var contractId in contractIds)
            contractsById[contractId] = await _contracts.GetByIdAsync(contractId, cancellationToken);

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
                End = b is FocusBlock displayBlock && displayBlock.SessionEndedAt is { } sessionEndedAt && sessionEndedAt < b.End
                    ? sessionEndedAt
                    : b.End,
                WebsiteFilterType = preset?.WebsiteFilterType ?? WebsiteFilterType.Blocklist,
                ContractId = b.ContractId,
                IsCompleted = b is FocusBlock fb && fb.RitualCompletedAt.HasValue,
                HasEnded = b.HasEnded(now),
                IsUnderInEffectContract = b.ContractId.HasValue && (contractsById.GetValueOrDefault(b.ContractId.Value)?.IsInEffect ?? false),
                ContractStrictnessRule = b.ContractId.HasValue ? contractsById.GetValueOrDefault(b.ContractId.Value)?.StrictnessRule : null,
                IsMissed = b is FocusBlock missedCheck && missedCheck.RitualCompletedAt is null && b.HasEnded(now)
            };
        }).ToList();
    }
}
