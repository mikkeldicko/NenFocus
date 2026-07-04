using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record BlockProfileDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];

    public BlockType ProfileType { get; init; }
    public WebsitePolicy WebsitePolicy { get; init; }
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public IReadOnlyList<string> Websites { get; init; } = [];

    public int AppCount => BlockedApps.Count;
    public int SiteCount => Websites.Count;

    public static BlockProfileDto FromEntity(BlockProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Tags = profile.Tags.ToList(),
        ProfileType = profile.ProfileType,
        WebsitePolicy = profile.WebsitePolicy,
        BlockedApps = profile.BlockedApps.Select(a => a.ProcessName).ToList(),
        Websites = profile.Websites.Select(s => s.Url).ToList()
    };
}
