using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

public sealed record ActivePolicySlice
{
    public Guid? ActiveSessionId { get; init; }
    public Moment SliceStart { get; init; }
    public Moment SliceEnd { get; init; }

    /// <summary>Every blocklist entry in effect (presets' and the session's own) — also what the hosts file writes.</summary>
    public IReadOnlySet<Website> BlockedSites { get; init; } = new HashSet<Website>();

    /// <summary>
    /// One set per active allowlist preset, kept apart: a site gets through only if every one of them
    /// covers it. Not intersected up front — coverage (youtube.com covers m.youtube.com) can't be
    /// precomputed as one set (Gap 112, C1). Empty means blocklist mode.
    /// </summary>
    public IReadOnlyList<IReadOnlySet<Website>> AllowLists { get; init; } = [];

    public IReadOnlySet<BlockedApp> BlockedApps { get; init; } = new HashSet<BlockedApp>();

    public WebsiteFilterType WebsiteFilterTypeMode =>
        AllowLists.Count > 0 ? WebsiteFilterType.Allowlist : WebsiteFilterType.Blocklist;

    // Deliberately only "what should be blocked right now." Whether a Focus Block should
    // push the user into the Ritual is a separate question with its own home —
    // FocusBlock.IsAwaitingRitual / GetFocusBlockAwaitingRitualQuery (Gap 62).
    public bool IsEmpty =>
        AllowLists.Count == 0 &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;

    /// <summary>
    /// Is this site blocked right now — the one answer enforcement asks (Gap 112, I2). A block beats
    /// an allow; with any allowlist active, everything is blocked that some allowlist doesn't cover.
    /// </summary>
    public bool Blocks(Website site) =>
        BlockedSites.Any(blocked => blocked.Covers(site))
        || AllowLists.Any(allowed => !allowed.Any(entry => entry.Covers(site)));

    /// <summary>The listed app a running process is, or null — the kill sweep asks this (Gap 112, C2).</summary>
    public BlockedApp? BlockedAppFor(string runningProcessName) =>
        BlockedApps.FirstOrDefault(app => app.Matches(runningProcessName));
}
