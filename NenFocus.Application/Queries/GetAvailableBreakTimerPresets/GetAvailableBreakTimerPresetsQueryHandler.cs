using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetAvailableBreakTimerPresets;

public sealed class GetAvailableBreakTimerPresetsQueryHandler
    : IQueryHandler<GetAvailableBreakTimerPresetsQuery, IReadOnlyList<BreakTimerPresetDto>>
{
    private readonly IBreakTimerPresetRepository _presets;
    private readonly IBreakScheduleCalculator _scheduleCalculator;

    public GetAvailableBreakTimerPresetsQueryHandler(
        IBreakTimerPresetRepository presets,
        IBreakScheduleCalculator scheduleCalculator)
    {
        _presets = presets;
        _scheduleCalculator = scheduleCalculator;
    }

    public async Task<IReadOnlyList<BreakTimerPresetDto>> HandleAsync(
        GetAvailableBreakTimerPresetsQuery query, CancellationToken ct)
    {
        var allPresets = await _presets.GetAllAsync(ct);

        return allPresets
            .Where(p => _scheduleCalculator
                .ComputeSchedule(p.BreakTimer, query.TotalDuration)
                .Any(phase => phase.Kind != SessionPhaseKind.Work))
            .Select(BreakTimerPresetDto.FromEntity)
            .ToList();
    }
}
