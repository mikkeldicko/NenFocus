using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;

public sealed record UpdateBlockPresetResult : ICommandResult<UpdateBlockPresetResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateBlockPresetResult Ok() => new() { Success = true };
    public static UpdateBlockPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
