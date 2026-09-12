using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.DeleteBreakTimerPreset;

public sealed record DeleteBreakTimerPresetResult : ICommandResult<DeleteBreakTimerPresetResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteBreakTimerPresetResult Ok() => new() { Success = true };
    public static DeleteBreakTimerPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
