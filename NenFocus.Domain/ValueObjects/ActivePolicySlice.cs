using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects; 

public sealed class ActivePolicySlice
{
    public Guid? ActiveSessionId { get; init; }
    public DateTime SliceStart { get; init; }
    public DateTime SliceEnd { get; init; }
    public WebsitePolicy WebsitePolicyMode { get; init; } = WebsitePolicy.Blocklist;

    public HashSet<string> AllowedSites { get; init; } = [];
    public HashSet<string> BlockedSites { get; init; } = [];
    public HashSet<string> BlockedApps { get; init; } = [];
    public BlockType? ActiveBlockType { get; init; }

    public bool IsEmpty =>
        WebsitePolicyMode == WebsitePolicy.Blocklist &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;
}