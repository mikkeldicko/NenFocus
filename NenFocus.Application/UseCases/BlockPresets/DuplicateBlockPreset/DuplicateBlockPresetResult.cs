using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;

public sealed record DuplicateBlockPresetResult : ICommandResult<DuplicateBlockPresetResult>
{
    public bool Success { get; init; }
    public Guid? NewPresetId { get; init; }
    public string? ErrorMessage { get; init; }

    public static DuplicateBlockPresetResult Ok(Guid id) =>
        new() { Success = true, NewPresetId = id };
    public static DuplicateBlockPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
