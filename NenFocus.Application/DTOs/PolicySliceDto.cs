using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed class PolicySliceDto
{
    public Guid? ActiveSessionId { get; init; }
    public DateTime SliceStart { get; init; }
    public DateTime SliceEnd { get; init; }

    // This is the renamed property using the Enum
    public WebsitePolicy WebsitePolicyMode { get; init; } = WebsitePolicy.Blocklist;

    public HashSet<string> AllowedSites { get; init; } = [];
    public HashSet<string> BlockedSites { get; init; } = [];
    public HashSet<string> BlockedApps { get; init; } = [];

    // Updated IsEmpty to handle the Enum logic
    public bool IsEmpty =>
        WebsitePolicyMode == WebsitePolicy.Blocklist &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;
}