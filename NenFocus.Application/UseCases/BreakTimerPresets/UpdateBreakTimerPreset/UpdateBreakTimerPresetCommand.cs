using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.UpdateBreakTimerPreset;

public sealed record UpdateBreakTimerPresetCommand : ICommand<UpdateBreakTimerPresetResult>
{
    public Guid PresetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int WorkMinutes { get; init; }
    public int ShortBreakMinutes { get; init; }
    public int CyclesBeforeLongBreak { get; init; }
    public int LongBreakMinutes { get; init; }
}
