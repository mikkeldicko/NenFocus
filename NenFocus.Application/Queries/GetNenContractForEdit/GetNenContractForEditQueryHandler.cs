using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractForEdit;

public sealed class GetNenContractForEditQueryHandler
    : IQueryHandler<GetNenContractForEditQuery, NenContractEditDto?>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;

    public GetNenContractForEditQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets)
    {
        _contracts = contracts;
        _blocks = blocks;
        _presets = presets;
    }

    public async Task<NenContractEditDto?> HandleAsync(
        GetNenContractForEditQuery query, CancellationToken cancellationToken = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, cancellationToken);
        if (contract is null)
            return null;

        var days = new List<NenContractEditDayDto>();
        for (var date = contract.StartDate; date <= contract.EndDate; date = date.AddDays(1))
        {
            var window = contract.WindowOn(date)!.Value;
            days.Add(new NenContractEditDayDto(date, window.Start, window.End, contract.FloorHoursFor(date)));
        }

        var linkedBlocks = await _blocks.GetForContractAsync(query.ContractId, cancellationToken);

        var presetIds = linkedBlocks.Select(b => b.BlockPresetId).Distinct();
        var presetNames = new Dictionary<Guid, string>();
        foreach (var presetId in presetIds)
        {
            var preset = await _presets.GetByIdAsync(presetId, cancellationToken);
            presetNames[presetId] = preset?.Name ?? "Unknown";
        }

        var blockDtos = linkedBlocks
            .OrderBy(b => b.Start)
            .Select(b => new NenContractEditBlockDto
            {
                PresetId = b.BlockPresetId,
                PresetName = presetNames.GetValueOrDefault(b.BlockPresetId, "Unknown"),
                Start = b.Start,
                End = b.End,
                IsFocus = b is FocusBlock,
                Title = b.Title
            })
            .ToList();

        return new NenContractEditDto
        {
            Id = contract.Id,
            Name = contract.Name,
            Goal = contract.Goal.Value,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            StrictnessRule = contract.StrictnessRule,
            AllowCancellation = contract.AllowCancellation,
            FixedDailyFloorHours = (contract.FloorRule as DailyFloorRule.Fixed)?.Floor.Hours,
            Days = days,
            ContractPresetId = contract.ContractPresetId,
            RedemptionPlacementPolicy = contract.RedemptionPlacementPolicy,
            KeepBlockingDuringBreaks = contract.KeepBlockingDuringBreaks,
            Blocks = blockDtos
        };
    }
}
