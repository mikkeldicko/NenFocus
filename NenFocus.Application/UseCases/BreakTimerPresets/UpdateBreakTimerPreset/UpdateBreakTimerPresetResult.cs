using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.UpdateBreakTimerPreset;

public sealed record UpdateBreakTimerPresetResult : ICommandResult<UpdateBreakTimerPresetResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateBreakTimerPresetResult Ok() => new() { Success = true };
    public static UpdateBreakTimerPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
