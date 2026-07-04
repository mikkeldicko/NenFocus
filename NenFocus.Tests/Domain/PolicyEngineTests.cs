using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class PolicyEngineTests
{
    private static readonly DateTime At = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly PolicyEngine _engine = new();

    private static BlockProfile CreateProfile(
        WebsitePolicy policy,
        IEnumerable<string>? apps = null,
        IEnumerable<string>? sites = null,
        BlockType type = BlockType.Focus)
    {
        var profile = BlockProfile.Create(Guid.NewGuid(), "Profile", policy, type, At);
        foreach (var app in apps ?? [])
            profile.AddBlockedApp(new BlockedApp(app));
        foreach (var site in sites ?? [])
            profile.AddWebsite(new BlockedSite(site));
        return profile;
    }

    private static Session CreateActiveSession(
        IReadOnlyList<string>? temporaryBlockedApps = null,
        IReadOnlyList<string>? temporaryBlockedSites = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            SessionOrigin.Spontaneous,
            temporaryBlockedApps: temporaryBlockedApps,
            temporaryBlockedSites: temporaryBlockedSites);
        session.Start(At);
        return session;
    }

    [Fact]
    public void ComputeSlice_NoProfilesAndNoSessions_ReturnsMinimalEmptySlice()
    {
        var slice = _engine.ComputeSlice(At, [], [], []);

        slice.SliceStart.Should().Be(At);
        slice.SliceEnd.Should().Be(At.AddMinutes(1));
        slice.BlockedApps.Should().BeEmpty();
        slice.BlockedSites.Should().BeEmpty();
        slice.ActiveBlockType.Should().BeNull();
        slice.ActiveSessionId.Should().BeNull();
        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_BlockPresentWithNoResolvableProfileOrSession_StillSurfacesActiveBlockType()
    {
        // An orphaned block (its profile could not be resolved, e.g. deleted
        // out from under it) must still be visible via ActiveBlockType so the
        // Ritual trigger and enforcement window aren't silently dropped — even
        // though there's no profile data left to populate BlockedApps/Sites.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [], [], [block]);

        slice.ActiveBlockType.Should().Be(BlockType.Focus);
        slice.ActiveFocusBlockId.Should().Be(block.Id);
        slice.SliceEnd.Should().Be(block.End);
        slice.BlockedApps.Should().BeEmpty();
        slice.BlockedSites.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_UnionsBlockedAppsAcrossProfilesAndNormalizesNames()
    {
        var profileA = CreateProfile(WebsitePolicy.Blocklist, apps: ["chrome.exe"]);
        var profileB = CreateProfile(WebsitePolicy.Blocklist, apps: ["Spotify"]);
        var session = CreateActiveSession(temporaryBlockedApps: ["notepad.exe"]);

        var slice = _engine.ComputeSlice(At, [profileA, profileB], [session], []);

        slice.BlockedApps.Should().BeEquivalentTo(["chrome", "spotify", "notepad"]);
    }

    [Fact]
    public void ComputeSlice_BlockedAppDedupeIsCaseInsensitive()
    {
        var profileA = CreateProfile(WebsitePolicy.Blocklist, apps: ["Chrome"]);
        var profileB = CreateProfile(WebsitePolicy.Blocklist, apps: ["chrome.exe"]);

        var slice = _engine.ComputeSlice(At, [profileA, profileB], [], []);

        slice.BlockedApps.Should().ContainSingle().Which.Should().Be("chrome");
    }

    [Fact]
    public void ComputeSlice_BlocklistMode_UnionsSitesAcrossProfiles()
    {
        var profileA = CreateProfile(WebsitePolicy.Blocklist, sites: ["facebook.com"]);
        var profileB = CreateProfile(WebsitePolicy.Blocklist, sites: ["youtube.com"]);

        var slice = _engine.ComputeSlice(At, [profileA, profileB], [], []);

        slice.WebsitePolicyMode.Should().Be(WebsitePolicy.Blocklist);
        slice.BlockedSites.Should().BeEquivalentTo(["facebook.com", "youtube.com"]);
        slice.AllowedSites.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_BlocklistMode_IncludesTemporaryBlockedSites()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist, sites: ["facebook.com"]);
        var session = CreateActiveSession(temporaryBlockedSites: ["reddit.com"]);

        var slice = _engine.ComputeSlice(At, [profile], [session], []);

        slice.BlockedSites.Should().BeEquivalentTo(["facebook.com", "reddit.com"]);
    }

    [Fact]
    public void ComputeSlice_AnyAllowlistProfile_SwitchesEffectivePolicyToAllowlist()
    {
        var profile = CreateProfile(WebsitePolicy.Allowlist, sites: ["work.com"]);

        var slice = _engine.ComputeSlice(At, [profile], [], []);

        slice.WebsitePolicyMode.Should().Be(WebsitePolicy.Allowlist);
        slice.AllowedSites.Should().BeEquivalentTo(["work.com"]);
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_MultipleAllowlistProfiles_IntersectsAllowedSites()
    {
        var profileA = CreateProfile(WebsitePolicy.Allowlist, sites: ["a.com", "b.com"]);
        var profileB = CreateProfile(WebsitePolicy.Allowlist, sites: ["b.com", "c.com"]);

        var slice = _engine.ComputeSlice(At, [profileA, profileB], [], []);

        slice.AllowedSites.Should().BeEquivalentTo(["b.com"]);
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_BlocklistProfileSitesAreRemovedFromAllowedAndAdded()
    {
        var allowProfile = CreateProfile(WebsitePolicy.Allowlist, sites: ["a.com", "b.com"]);
        var blockProfile = CreateProfile(WebsitePolicy.Blocklist, sites: ["b.com"]);

        var slice = _engine.ComputeSlice(At, [allowProfile, blockProfile], [], []);

        slice.AllowedSites.Should().BeEquivalentTo(["a.com"]);
        slice.BlockedSites.Should().BeEquivalentTo(["b.com"]);
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_TemporaryBlockedSiteOverridesAllowlistMembership()
    {
        var allowProfile = CreateProfile(WebsitePolicy.Allowlist, sites: ["a.com", "temp.com"]);
        var session = CreateActiveSession(temporaryBlockedSites: ["temp.com"]);

        var slice = _engine.ComputeSlice(At, [allowProfile], [session], []);

        slice.AllowedSites.Should().BeEquivalentTo(["a.com"]);
        slice.BlockedSites.Should().BeEquivalentTo(["temp.com"]);
    }

    [Fact]
    public void ComputeSlice_FocusBlockActive_SetsActiveBlockTypeAndFocusBlockId()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [profile], [], [focusBlock]);

        slice.ActiveBlockType.Should().Be(BlockType.Focus);
        slice.ActiveFocusBlockId.Should().Be(focusBlock.Id);
    }

    [Fact]
    public void ComputeSlice_BackgroundBlockActive_SetsActiveBlockTypeButNoFocusBlockId()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var backgroundBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [profile], [], [backgroundBlock]);

        slice.ActiveBlockType.Should().Be(BlockType.Background);
        slice.ActiveFocusBlockId.Should().BeNull();
    }

    [Fact]
    public void ComputeSlice_FocusAndBackgroundBothActive_FocusTakesPrecedence()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        var backgroundBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [profile], [], [backgroundBlock, focusBlock]);

        slice.ActiveBlockType.Should().Be(BlockType.Focus);
    }

    [Fact]
    public void ComputeSlice_NoBlocks_SliceEndIsOneMinuteAfterAt()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);

        var slice = _engine.ComputeSlice(At, [profile], [], []);

        slice.SliceEnd.Should().Be(At.AddMinutes(1));
    }

    [Fact]
    public void ComputeSlice_WithBlocks_SliceEndIsLatestBlockEnd()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var earlierBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddMinutes(30));
        var laterBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(2));

        var slice = _engine.ComputeSlice(At, [profile], [], [earlierBlock, laterBlock]);

        slice.SliceEnd.Should().Be(At.AddHours(2));
    }

    [Fact]
    public void ComputeSlice_FocusBlockRitualCompletedAndNoActiveSessions_FocusSessionCompletedIsTrue()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        focusBlock.MarkRitualCompleted(At);

        var slice = _engine.ComputeSlice(At, [profile], [], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_FocusBlockRitualCompletedButSessionStillActive_FocusSessionCompletedIsFalse()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        focusBlock.MarkRitualCompleted(At);
        var session = CreateActiveSession();

        var slice = _engine.ComputeSlice(At, [profile], [session], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeFalse();
    }

    [Fact]
    public void ComputeSlice_FocusBlockRitualNotCompleted_FocusSessionCompletedIsFalse()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [profile], [], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeFalse();
    }

    [Fact]
    public void ComputeSlice_ActiveSessionPresent_SetsActiveSessionId()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        var session = CreateActiveSession();

        var slice = _engine.ComputeSlice(At, [profile], [session], []);

        slice.ActiveSessionId.Should().Be(session.Id);
    }

    [Fact]
    public void ComputeSlice_BlocklistWithNoBlockedAppsOrSites_IsEmpty()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);

        var slice = _engine.ComputeSlice(At, [profile], [], []);

        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_IsNeverEmpty()
    {
        var profile = CreateProfile(WebsitePolicy.Allowlist);

        var slice = _engine.ComputeSlice(At, [profile], [], []);

        slice.IsEmpty.Should().BeFalse();
    }
}
