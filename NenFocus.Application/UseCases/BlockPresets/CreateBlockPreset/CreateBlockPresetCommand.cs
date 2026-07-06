using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;

public sealed record CreateBlockPresetCommand : ICommand<CreateBlockPresetResult>
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public WebsiteFilterType WebsiteFilterType { get; init; }
    public BlockPresetType PresetType { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];
}