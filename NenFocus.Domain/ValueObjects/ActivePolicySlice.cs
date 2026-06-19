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

    // True when a Ritual Block is still within its scheduled window but its session has
    // already ended. Used by ScheduledEnforcementService to stop re-applying enforcement
    // after the user ends their session early.
    public bool RitualSessionCompleted { get; init; }

    public bool IsEmpty =>
        WebsitePolicyMode == WebsitePolicy.Blocklist &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;
}