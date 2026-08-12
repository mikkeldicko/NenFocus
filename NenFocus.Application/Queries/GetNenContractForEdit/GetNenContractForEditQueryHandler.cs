using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

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

        var isAdvanced = contract.DailyFloorPolicy.Default is not null;

        var restDays = isAdvanced
            ? contract.DailyFloorPolicy.PerDayExceptions.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList()
            : (IReadOnlyList<DateOnly>)[];

        var noWorkHoursDays = contract.WorkHoursPolicy?.PerDayExceptions
            .Where(kv => kv.Value is null).Select(kv => kv.Key).ToList() ?? [];

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
            IsAdvanced = isAdvanced,
            FixedDailyFloorHours = contract.DailyFloorPolicy.Default?.Hours,
            RestDays = restDays,
            WorkHoursStart = contract.WorkHoursPolicy?.Default?.Start,
            WorkHoursEnd = contract.WorkHoursPolicy?.Default?.End,
            NoWorkHoursDays = noWorkHoursDays,
            ContractPresetId = contract.ContractPresetId,
            Blocks = blockDtos
        };
    }
}
