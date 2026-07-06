using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;

public sealed record DeleteBlockPresetResult : ICommandResult<DeleteBlockPresetResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteBlockPresetResult Ok() => new() { Success = true };
    public static DeleteBlockPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
