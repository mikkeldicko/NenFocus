using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class BlockPreset : AggregateRoot
{
    private readonly List<BlockedApp> _blockedApps = [];
    private readonly List<BlockedSite> _websites = [];
    private readonly List<string> _tags = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public WebsiteFilterType WebsiteFilterType { get; private set; }
    public BlockPresetType PresetType { get; private set; }

    public IReadOnlyList<BlockedApp> BlockedApps => _blockedApps.AsReadOnly();
    public IReadOnlyList<BlockedSite> Websites => _websites.AsReadOnly();
    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    private BlockPreset() { }

    public static BlockPreset Create(
        Guid id,
        string name,
        WebsiteFilterType websiteFilterType,
        BlockPresetType presetType,
        DateTime createdAt,
        IEnumerable<string>? tags = null)
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

        if (tags is not null)
            foreach (var tag in tags)
                preset._tags.Add(tag.Trim());

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

    public void AddTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (_tags.Contains(tag.Trim())) return;
        _tags.Add(tag.Trim());
    }

    public void RemoveTag(string tag)
    {
        _tags.Remove(tag.Trim());
    }

    public BlockPreset Duplicate(string newName, Guid newId, DateTime duplicatedAt)
    {
        var copy = Create(newId, newName, WebsiteFilterType, PresetType, duplicatedAt, _tags);
        foreach (var app in _blockedApps)
            copy.AddBlockedApp(new BlockedApp(app.ProcessName));
        foreach (var site in _websites)
            copy.AddWebsite(new BlockedSite(site.Url));
        return copy;
    }

}
