using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAvailableBreakTimerPresets;

public sealed class GetAvailableBreakTimerPresetsQueryHandler
    : IQueryHandler<GetAvailableBreakTimerPresetsQuery, IReadOnlyList<BreakTimerPresetDto>>
{
    private readonly IBreakTimerPresetRepository _presets;

    public GetAvailableBreakTimerPresetsQueryHandler(IBreakTimerPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<IReadOnlyList<BreakTimerPresetDto>> HandleAsync(
        GetAvailableBreakTimerPresetsQuery query, CancellationToken ct)
    {
        var allPresets = await _presets.GetAllAsync(ct);

        return allPresets
            .Where(p => !p.BreakTimer.ScheduleFor(query.TotalDuration).IsEmpty)
            .Select(BreakTimerPresetDto.FromEntity)
            .ToList();
    }
}
