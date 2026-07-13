using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllContractPresets;

public sealed class GetAllContractPresetsQueryHandler
    : IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>
{
    private readonly IContractPresetRepository _presets;

    public GetAllContractPresetsQueryHandler(IContractPresetRepository presets)
        => _presets = presets;

    public async Task<IReadOnlyList<ContractPresetDto>> HandleAsync(
        GetAllContractPresetsQuery query,
        CancellationToken cancellationToken = default)
    {
        var presets = await _presets.GetAllAsync(cancellationToken);
        return presets.Select(ContractPresetDto.FromEntity).ToList();
    }
}
