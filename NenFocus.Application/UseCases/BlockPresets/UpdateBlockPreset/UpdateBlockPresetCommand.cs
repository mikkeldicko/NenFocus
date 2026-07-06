using NenFocus.Domain.Enums;
using System;
using System.Collections.Generic;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;

public sealed record UpdateBlockPresetCommand : ICommand<UpdateBlockPresetResult>
{
    public Guid PresetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsiteFilterType WebsiteFilterType { get; init; }
    public BlockPresetType PresetType { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}
