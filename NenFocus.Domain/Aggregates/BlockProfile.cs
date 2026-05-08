using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Aggregates;

public class BlockProfile : BaseEntity
{
    private readonly List<BlockedApp> _blockedApps = new();
    private readonly List<BlockedSite> _websites = new();
    private readonly List<string> _tags = new();

    public string Name { get; private set; } = null!;
    public WebsitePolicy WebsitePolicy { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyList<BlockedApp> BlockedApps => _blockedApps.AsReadOnly();
    public IReadOnlyList<BlockedSite> Websites => _websites.AsReadOnly();
    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    private BlockProfile() { }

    public static BlockProfile Create(
        string name,
        WebsitePolicy websitePolicy,
        IEnumerable<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name cannot be empty.");

        var profile = new BlockProfile
        {
            Name = name.Trim(),
            WebsitePolicy = websitePolicy,
            IsActive = true
        };

        if (tags != null)
            foreach (var tag in tags)
                profile._tags.Add(tag.Trim());

        return profile;
    }
    
    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Profile name cannot be empty.");

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

    public void ChangeWebsitePolicy(WebsitePolicy policy)
    {
        WebsitePolicy = policy;
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

    public BlockProfile Duplicate(string newName)
    {
        var copy = Create(newName, WebsitePolicy, _tags);
        foreach (var app in _blockedApps) copy.AddBlockedApp(new BlockedApp(app.ProcessName));
        foreach (var site in _websites) copy.AddWebsite(new BlockedSite(site.Url));
        return copy;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}