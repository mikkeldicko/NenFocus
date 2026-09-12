using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllBreakTimerPresets;

public sealed class GetAllBreakTimerPresetsQueryHandler
    : IQueryHandler<GetAllBreakTimerPresetsQuery, IReadOnlyList<BreakTimerPresetDto>>
{
    private readonly IBreakTimerPresetRepository _presets;

    public GetAllBreakTimerPresetsQueryHandler(IBreakTimerPresetRepository presets)
        => _presets = presets;

    public async Task<IReadOnlyList<BreakTimerPresetDto>> HandleAsync(
        GetAllBreakTimerPresetsQuery query,
        CancellationToken cancellationToken = default)
    {
        var presets = await _presets.GetAllAsync(cancellationToken);
        return presets.Select(BreakTimerPresetDto.FromEntity).ToList();
    }
}
