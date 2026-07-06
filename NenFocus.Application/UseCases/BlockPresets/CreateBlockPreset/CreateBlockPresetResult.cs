using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;

public sealed record CreateBlockPresetResult : ICommandResult<CreateBlockPresetResult>
{
    public bool Success { get; init; }
    public Guid? PresetId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateBlockPresetResult Ok(Guid id) =>
        new() { Success = true, PresetId = id };
    public static CreateBlockPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
