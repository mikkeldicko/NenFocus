namespace NenFocus.Application.DTOs;

/// <summary>
/// Represents a block profile for display in the Ren page profile list
/// and the Block Profiles management page.
/// </summary>
public sealed record BlockProfileDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>"Blocklist" or "Allowlist"</summary>
    public string WebsitePolicy { get; init; } = string.Empty;

    public IReadOnlyList<string> BlockedApps { get; init; } = [];

    /// <summary>
    /// Semantics depend on WebsitePolicy:
    /// Blocklist → sites to block. Allowlist → only sites permitted.
    /// </summary>
    public IReadOnlyList<string> Websites { get; init; } = [];

    /// <summary>Convenience counts for profile cards in the UI.</summary>
    public int AppCount => BlockedApps.Count;
    public int SiteCount => Websites.Count;
}