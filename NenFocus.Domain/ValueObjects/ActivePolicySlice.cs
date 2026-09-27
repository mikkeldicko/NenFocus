using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

public sealed record ActivePolicySlice
{
    public Guid? ActiveSessionId { get; init; }
    public DateTime SliceStart { get; init; }
    public DateTime SliceEnd { get; init; }
    public WebsiteFilterType WebsiteFilterTypeMode { get; init; } = WebsiteFilterType.Blocklist;

    public IReadOnlySet<string> AllowedSites { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedSites { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedApps  { get; init; } = new HashSet<string>();

    // Deliberately only "what should be blocked right now." Whether a Focus Block should
    // push the user into the Ritual is a separate question with its own home —
    // FocusBlock.IsAwaitingRitual / GetFocusBlockAwaitingRitualQuery (Gap 62).
    public bool IsEmpty =>
        WebsiteFilterTypeMode == WebsiteFilterType.Blocklist &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;
}
