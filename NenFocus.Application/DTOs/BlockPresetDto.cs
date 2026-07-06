using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record BlockPresetDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];

    public BlockPresetType PresetType { get; init; }
    public WebsiteFilterType WebsiteFilterType { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];

    public int AppCount => BlockedApps.Count;
    public int SiteCount => Websites.Count;

    public static BlockPresetDto FromEntity(BlockPreset preset) => new()
    {
        Id = preset.Id,
        Name = preset.Name,
        Tags = preset.Tags.ToList(),
        PresetType = preset.PresetType,
        WebsiteFilterType = preset.WebsiteFilterType,
        BlockedApps = preset.BlockedApps.Select(a => a.ProcessName).ToList(),
        Websites = preset.Websites.Select(s => s.Url).ToList()
    };
}
