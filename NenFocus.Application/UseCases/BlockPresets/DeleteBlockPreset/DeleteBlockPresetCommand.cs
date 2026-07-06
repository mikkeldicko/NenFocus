using System;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;

public sealed record DeleteBlockPresetCommand : ICommand<DeleteBlockPresetResult>
{
    public Guid PresetId { get; init; }
}