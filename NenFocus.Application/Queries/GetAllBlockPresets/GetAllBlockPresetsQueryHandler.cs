using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllBlockPresets;

public sealed class GetAllBlockPresetsQueryHandler
    : IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>>
{
    private readonly IBlockPresetRepository _presets;

    public GetAllBlockPresetsQueryHandler(IBlockPresetRepository presets)
        => _presets = presets;

    public async Task<IReadOnlyList<BlockPresetDto>> HandleAsync(
        GetAllBlockPresetsQuery query,
        CancellationToken cancellationToken = default)
    {
        var presets = string.IsNullOrWhiteSpace(query.Tag)
            ? await _presets.GetAllAsync(cancellationToken)
            : await _presets.GetByTagsAsync([query.Tag], cancellationToken);

        IEnumerable<Domain.Entities.BlockPreset> filtered = presets;
        if (query.PresetType.HasValue)
            filtered = presets.Where(p => p.PresetType == query.PresetType.Value);

        return filtered.Select(BlockPresetDto.FromEntity).ToList();
    }
}
