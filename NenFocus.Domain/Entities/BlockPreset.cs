using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class BlockPreset : AggregateRoot
{
    private readonly List<BlockedApp> _blockedApps = [];
    private readonly List<BlockedSite> _websites = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public WebsiteFilterType WebsiteFilterType { get; private set; }
    public BlockPresetType PresetType { get; private set; }

    public IReadOnlyList<BlockedApp> BlockedApps => _blockedApps.AsReadOnly();
    public IReadOnlyList<BlockedSite> Websites => _websites.AsReadOnly();

    private BlockPreset() { }

    public static BlockPreset Create(
        Guid id,
        string name,
        WebsiteFilterType websiteFilterType,
        BlockPresetType presetType,
        Moment createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Preset name cannot be empty.", nameof(name));

        var preset = new BlockPreset
        {
            Id = id,
            Name = name.Trim(),
            WebsiteFilterType = websiteFilterType,
            PresetType = presetType
        };

        preset.Raise(new BlockPresetCreatedEvent(id, name.Trim(), createdAt));

        return preset;
    }

    public void ChangePresetType(BlockPresetType newType) => PresetType = newType;

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException(
                "Preset name cannot be empty.", nameof(newName));

        Name = newName.Trim();
    }

    public void AddBlockedApp(BlockedApp app)
    {
        if (_blockedApps.Any(a => a.ProcessName == app.ProcessName))
            return;

        _blockedApps.Add(app);
    }

    public void RemoveBlockedApp(BlockedApp app)
    {
        _blockedApps.RemoveAll(a => a.ProcessName == app.ProcessName);
    }

    public void AddWebsite(BlockedSite site)
    {
        if (_websites.Any(s => s.Url == site.Url))
            return;

        _websites.Add(site);
    }

    public void RemoveWebsite(BlockedSite site)
    {
        _websites.RemoveAll(s => s.Url == site.Url);
    }

    public void ChangeWebsiteFilterType(WebsiteFilterType policy)
    {
        WebsiteFilterType = policy;
        _websites.Clear();
    }

    /// <summary>
    /// True when the proposed apps/sites keep this preset at least as strict as it currently
    /// is — the invariant a locked preset (in use by an active session or a scheduled block)
    /// must never violate. Apps have no allow-mode, so dropping a blocked app always weakens
    /// the preset: apps must only be added. Sites invert by WebsiteFilterType — under
    /// Blocklist, the same add-only rule applies (removing a blocked site weakens it); under
    /// Allowlist it's the reverse (adding a site widens what's let through, weakening it;
    /// removing one narrows it, strengthening it), so sites there must only be removed.
    /// </summary>
    public bool IsAtLeastAsStrict(IReadOnlySet<string> proposedApps, IReadOnlySet<string> proposedSites)
    {
        var currentApps = _blockedApps.Select(a => a.ProcessName).ToHashSet();
        if (!currentApps.IsSubsetOf(proposedApps)) return false;

        var currentSites = _websites.Select(s => s.Url).ToHashSet();
        return WebsiteFilterType == WebsiteFilterType.Allowlist
            ? proposedSites.IsSubsetOf(currentSites)
            : currentSites.IsSubsetOf(proposedSites);
    }

    public BlockPreset Duplicate(string newName, Guid newId, Moment duplicatedAt)
    {
        var copy = Create(newId, newName, WebsiteFilterType, PresetType, duplicatedAt);
        foreach (var app in _blockedApps)
            copy.AddBlockedApp(new BlockedApp(app.ProcessName));
        foreach (var site in _websites)
            copy.AddWebsite(new BlockedSite(site.Url));
        return copy;
    }

}
