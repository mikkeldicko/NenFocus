using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;

public sealed record DuplicateBlockPresetCommand : ICommand<DuplicateBlockPresetResult>
{
    public Guid SourcePresetId { get; init; }
    public string? NewName { get; init; }
}