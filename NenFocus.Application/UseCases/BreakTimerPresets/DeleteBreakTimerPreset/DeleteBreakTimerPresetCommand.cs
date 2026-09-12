using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.DeleteBreakTimerPreset;

public sealed record DeleteBreakTimerPresetCommand : ICommand<DeleteBreakTimerPresetResult>
{
    public Guid PresetId { get; init; }
}
