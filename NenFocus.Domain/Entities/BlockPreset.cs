using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class BlockPreset : AggregateRoot
{
    private readonly List<BlockedApp> _blockedApps = [];
    private readonly List<Website> _websites = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public WebsiteFilterType WebsiteFilterType { get; private set; }
    public BlockPresetType PresetType { get; private set; }

    public IReadOnlyList<BlockedApp> BlockedApps => _blockedApps.AsReadOnly();
    public IReadOnlyList<Website> Websites => _websites.AsReadOnly();

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
            Name = NameAsSaved(name),
            WebsiteFilterType = websiteFilterType,
            PresetType = presetType
        };

        preset.Raise(new BlockPresetCreatedEvent(id, NameAsSaved(name), createdAt));

        return preset;
    }

    public void ChangePresetType(BlockPresetType newType) => PresetType = newType;

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException(
                "Preset name cannot be empty.", nameof(newName));

        Name = NameAsSaved(newName);
    }

    /// <summary>A name as the preset keeps it — the one normalisation Create, Rename and AllowsChangeWhileLocked share.</summary>
    private static string NameAsSaved(string name) => name.Trim();

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

    public void AddWebsite(Website site)
    {
        if (_websites.Any(s => s.Url == site.Url))
            return;

        _websites.Add(site);
    }

    public void RemoveWebsite(Website site)
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
    /// Compared by entry, not by coverage (Gap 112): swapping google.com for mail.google.com on an
    /// allowlist is in fact stricter but is refused — conservative, never lets a weakening through.
    /// </summary>
    public bool IsAtLeastAsStrict(IReadOnlySet<BlockedApp> proposedApps, IReadOnlySet<Website> proposedSites)
    {
        if (!_blockedApps.ToHashSet().IsSubsetOf(proposedApps)) return false;

        var currentSites = _websites.ToHashSet();
        return WebsiteFilterType == WebsiteFilterType.Allowlist
            ? proposedSites.IsSubsetOf(currentSites)
            : currentSites.IsSubsetOf(proposedSites);
    }

    /// <summary>
    /// Locked at <paramref name="now"/> — it can't be deleted, and may only change as
    /// <see cref="AllowsChangeWhileLocked"/> allows — while anything still depends on it: the running
    /// session uses it; a block that uses it hasn't really ended (its ReservedSlot — so an early end or a
    /// cancellation frees it); or a contract in effect still owes time of it (Cover Deficit places a new
    /// block with it). The one rule both preset validators ask, through CheckBlockPresetLockedQuery (Gap 89
    /// Slice D). <paramref name="blocksUsingIt"/> must be its own — another preset's block throws;
    /// <paramref name="openDeficits"/> may be any contract's, of any preset: only its own count.
    /// </summary>
    public bool IsLockedAt(Moment now, Session? running, IEnumerable<PlannedBlock> blocksUsingIt,
        IEnumerable<OutstandingDeficit> openDeficits)
    {
        var blocks = blocksUsingIt.ToList();
        if (blocks.Any(b => b.BlockPresetId != Id))
            throw new ArgumentException("A block of another preset was passed in.", nameof(blocksUsingIt));

        return running?.BlockPresetId == Id
            || blocks.Any(b => b.ReservedSlot(now) is { } reserved && !reserved.HasEnded(now))
            || openDeficits.Any(d => d.PresetId == Id);
    }

    /// <summary>
    /// What may change while it is locked: the name (as Rename saves it), filter and type unchanged, and
    /// the lists at least as strict (<see cref="IsAtLeastAsStrict"/>) — a commitment can be strengthened,
    /// never weakened, renamed or retyped (Gaps 8, 30; moved here from UpdateBlockPresetValidator, Gap 89
    /// Slice D).
    /// </summary>
    public bool AllowsChangeWhileLocked(string name, WebsiteFilterType filterType, BlockPresetType presetType,
        IReadOnlySet<BlockedApp> apps, IReadOnlySet<Website> sites) =>
        NameAsSaved(name) == Name && filterType == WebsiteFilterType && presetType == PresetType
        && IsAtLeastAsStrict(apps, sites);

    public BlockPreset Duplicate(string newName, Guid newId, Moment duplicatedAt)
    {
        var copy = Create(newId, newName, WebsiteFilterType, PresetType, duplicatedAt);
        foreach (var app in _blockedApps)
            copy.AddBlockedApp(new BlockedApp(app.ProcessName));
        foreach (var site in _websites)
            copy.AddWebsite(new Website(site.Url));
        return copy;
    }

}
