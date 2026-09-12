using NenFocus.Domain.Entities;

namespace NenFocus.Application.DTOs;

public sealed record BreakTimerPresetDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int WorkMinutes { get; init; }
    public int ShortBreakMinutes { get; init; }
    public int CyclesBeforeLongBreak { get; init; }
    public int LongBreakMinutes { get; init; }

    public static BreakTimerPresetDto FromEntity(BreakTimerPreset preset) => new()
    {
        Id = preset.Id,
        Name = preset.Name,
        WorkMinutes = preset.BreakTimer.WorkMinutes,
        ShortBreakMinutes = preset.BreakTimer.ShortBreakMinutes,
        CyclesBeforeLongBreak = preset.BreakTimer.CyclesBeforeLongBreak,
        LongBreakMinutes = preset.BreakTimer.LongBreakMinutes
    };
}
