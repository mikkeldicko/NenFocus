using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class BlockProfile : IHasDomainEvents
{
    private readonly List<BlockedApp> _blockedApps = [];
    private readonly List<BlockedSite> _websites = [];
    private readonly List<string> _tags = [];
    private readonly List<DomainEvent> _domainEvents = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public WebsitePolicy WebsitePolicy { get; private set; }
    public BlockType ProfileType { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyList<BlockedApp> BlockedApps => _blockedApps.AsReadOnly();
    public IReadOnlyList<BlockedSite> Websites => _websites.AsReadOnly();
    public IReadOnlyList<string> Tags => _tags.AsReadOnly();
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private BlockProfile() { } // EF Core

    public static BlockProfile Create(
        Guid id,
        string name,
        WebsitePolicy websitePolicy,
        BlockType profileType,
        DateTime createdAt,
        IEnumerable<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Profile name cannot be empty.", nameof(name));

        var profile = new BlockProfile
        {
            Id = id,
            Name = name.Trim(),
            WebsitePolicy = websitePolicy,
            ProfileType = profileType,
            IsActive = true
        };

        if (tags is not null)
            foreach (var tag in tags)
                profile._tags.Add(tag.Trim());

        profile._domainEvents.Add(new BlockProfileCreatedEvent(id, name.Trim(), createdAt));

        return profile;
    }

    public void ChangeProfileType(BlockType newType) => ProfileType = newType;

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException(
                "Profile name cannot be empty.", nameof(newName));

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

    public BlockProfile Duplicate(string newName, Guid newId, DateTime duplicatedAt)
    {
        var copy = Create(newId, newName, WebsitePolicy, ProfileType, duplicatedAt, _tags);
        foreach (var app in _blockedApps)
            copy.AddBlockedApp(new BlockedApp(app.ProcessName));
        foreach (var site in _websites)
            copy.AddWebsite(new BlockedSite(site.Url));
        return copy;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
