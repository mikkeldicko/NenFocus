using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.CreateBreakTimerPreset;

public sealed record CreateBreakTimerPresetResult : ICommandResult<CreateBreakTimerPresetResult>
{
    public bool Success { get; init; }
    public Guid? PresetId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateBreakTimerPresetResult Ok(Guid id) =>
        new() { Success = true, PresetId = id };
    public static CreateBreakTimerPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
