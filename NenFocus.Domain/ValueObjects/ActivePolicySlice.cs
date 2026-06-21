using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

public sealed record ActivePolicySlice
{
    public Guid? ActiveSessionId { get; init; }
    public DateTime SliceStart { get; init; }
    public DateTime SliceEnd { get; init; }
    public WebsitePolicy WebsitePolicyMode { get; init; } = WebsitePolicy.Blocklist;

    public IReadOnlySet<string> AllowedSites { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedSites { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedApps  { get; init; } = new HashSet<string>();
    public BlockType? ActiveBlockType { get; init; }

    // True when a Ritual Block is still within its scheduled window but its session has
    // already ended — stops ScheduledEnforcementService from re-applying enforcement.
    public bool RitualSessionCompleted { get; init; }

    public bool IsEmpty =>
        WebsitePolicyMode == WebsitePolicy.Blocklist &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;
}
