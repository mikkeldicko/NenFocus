using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.CreateBreakTimerPreset;

public sealed record CreateBreakTimerPresetCommand : ICommand<CreateBreakTimerPresetResult>
{
    public string Name { get; init; } = string.Empty;
    public int WorkMinutes { get; init; }
    public int ShortBreakMinutes { get; init; }
    public int CyclesBeforeLongBreak { get; init; }
    public int LongBreakMinutes { get; init; }
}
