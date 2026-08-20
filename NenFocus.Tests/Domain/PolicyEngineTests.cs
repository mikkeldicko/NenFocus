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

    private static BlockPreset CreatePreset(
        WebsiteFilterType policy,
        IEnumerable<string>? apps = null,
        IEnumerable<string>? sites = null,
        BlockPresetType type = BlockPresetType.Focus)
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Preset", policy, type, At);
        foreach (var app in apps ?? [])
            preset.AddBlockedApp(new BlockedApp(app));
        foreach (var site in sites ?? [])
            preset.AddWebsite(new BlockedSite(site));
        return preset;
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
    public void ComputeSlice_NoPresetsAndNoSessions_ReturnsMinimalEmptySlice()
    {
        var slice = _engine.ComputeSlice(At, [], [], []);

        slice.SliceStart.Should().Be(At);
        slice.SliceEnd.Should().Be(At.AddMinutes(1));
        slice.BlockedApps.Should().BeEmpty();
        slice.BlockedSites.Should().BeEmpty();
        slice.ActiveBlockPresetType.Should().BeNull();
        slice.ActiveSessionId.Should().BeNull();
        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_BlockPresentWithNoResolvablePresetOrSession_StillSurfacesActiveBlockPresetType()
    {
        // An orphaned block (its preset could not be resolved, e.g. deleted
        // out from under it) must still be visible via ActiveBlockPresetType so the
        // Ritual trigger and enforcement window aren't silently dropped — even
        // though there's no preset data left to populate BlockedApps/Sites.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [], [], [block]);

        slice.ActiveBlockPresetType.Should().Be(BlockPresetType.Focus);
        slice.ActiveFocusBlockId.Should().Be(block.Id);
        slice.SliceEnd.Should().Be(block.End);
        slice.BlockedApps.Should().BeEmpty();
        slice.BlockedSites.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_UnionsBlockedAppsAcrossPresetsAndNormalizesNames()
    {
        var presetA = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var presetB = CreatePreset(WebsiteFilterType.Blocklist, apps: ["Spotify"]);
        var session = CreateActiveSession(temporaryBlockedApps: ["notepad.exe"]);

        var slice = _engine.ComputeSlice(At, [presetA, presetB], [session], []);

        slice.BlockedApps.Should().BeEquivalentTo(["chrome", "spotify", "notepad"]);
    }

    [Fact]
    public void ComputeSlice_BlockedAppDedupeIsCaseInsensitive()
    {
        var presetA = CreatePreset(WebsiteFilterType.Blocklist, apps: ["Chrome"]);
        var presetB = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);

        var slice = _engine.ComputeSlice(At, [presetA, presetB], [], []);

        slice.BlockedApps.Should().ContainSingle().Which.Should().Be("chrome");
    }

    [Fact]
    public void ComputeSlice_BlocklistMode_UnionsSitesAcrossPresets()
    {
        var presetA = CreatePreset(WebsiteFilterType.Blocklist, sites: ["facebook.com"]);
        var presetB = CreatePreset(WebsiteFilterType.Blocklist, sites: ["youtube.com"]);

        var slice = _engine.ComputeSlice(At, [presetA, presetB], [], []);

        slice.WebsiteFilterTypeMode.Should().Be(WebsiteFilterType.Blocklist);
        slice.BlockedSites.Should().BeEquivalentTo(["facebook.com", "youtube.com"]);
        slice.AllowedSites.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_BlocklistMode_IncludesTemporaryBlockedSites()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["facebook.com"]);
        var session = CreateActiveSession(temporaryBlockedSites: ["reddit.com"]);

        var slice = _engine.ComputeSlice(At, [preset], [session], []);

        slice.BlockedSites.Should().BeEquivalentTo(["facebook.com", "reddit.com"]);
    }

    [Fact]
    public void ComputeSlice_AnyAllowlistPreset_SwitchesEffectivePolicyToAllowlist()
    {
        var preset = CreatePreset(WebsiteFilterType.Allowlist, sites: ["work.com"]);

        var slice = _engine.ComputeSlice(At, [preset], [], []);

        slice.WebsiteFilterTypeMode.Should().Be(WebsiteFilterType.Allowlist);
        slice.AllowedSites.Should().BeEquivalentTo(["work.com"]);
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_MultipleAllowlistPresets_IntersectsAllowedSites()
    {
        var presetA = CreatePreset(WebsiteFilterType.Allowlist, sites: ["a.com", "b.com"]);
        var presetB = CreatePreset(WebsiteFilterType.Allowlist, sites: ["b.com", "c.com"]);

        var slice = _engine.ComputeSlice(At, [presetA, presetB], [], []);

        slice.AllowedSites.Should().BeEquivalentTo(["b.com"]);
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_BlocklistPresetSitesAreRemovedFromAllowedAndAdded()
    {
        var allowPreset = CreatePreset(WebsiteFilterType.Allowlist, sites: ["a.com", "b.com"]);
        var blockPreset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["b.com"]);

        var slice = _engine.ComputeSlice(At, [allowPreset, blockPreset], [], []);

        slice.AllowedSites.Should().BeEquivalentTo(["a.com"]);
        slice.BlockedSites.Should().BeEquivalentTo(["b.com"]);
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_TemporaryBlockedSiteOverridesAllowlistMembership()
    {
        var allowPreset = CreatePreset(WebsiteFilterType.Allowlist, sites: ["a.com", "temp.com"]);
        var session = CreateActiveSession(temporaryBlockedSites: ["temp.com"]);

        var slice = _engine.ComputeSlice(At, [allowPreset], [session], []);

        slice.AllowedSites.Should().BeEquivalentTo(["a.com"]);
        slice.BlockedSites.Should().BeEquivalentTo(["temp.com"]);
    }

    [Fact]
    public void ComputeSlice_FocusBlockActive_SetsActiveBlockPresetTypeAndFocusBlockId()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [preset], [], [focusBlock]);

        slice.ActiveBlockPresetType.Should().Be(BlockPresetType.Focus);
        slice.ActiveFocusBlockId.Should().Be(focusBlock.Id);
    }

    [Fact]
    public void ComputeSlice_BackgroundBlockActive_SetsActiveBlockPresetTypeButNoFocusBlockId()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var backgroundBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [preset], [], [backgroundBlock]);

        slice.ActiveBlockPresetType.Should().Be(BlockPresetType.Background);
        slice.ActiveFocusBlockId.Should().BeNull();
    }

    [Fact]
    public void ComputeSlice_FocusAndBackgroundBothActive_FocusTakesPrecedence()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        var backgroundBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [preset], [], [backgroundBlock, focusBlock]);

        slice.ActiveBlockPresetType.Should().Be(BlockPresetType.Focus);
    }

    [Fact]
    public void ComputeSlice_NoBlocks_SliceEndIsOneMinuteAfterAt()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);

        var slice = _engine.ComputeSlice(At, [preset], [], []);

        slice.SliceEnd.Should().Be(At.AddMinutes(1));
    }

    [Fact]
    public void ComputeSlice_WithBlocks_SliceEndIsLatestBlockEnd()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var earlierBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddMinutes(30));
        var laterBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(2));

        var slice = _engine.ComputeSlice(At, [preset], [], [earlierBlock, laterBlock]);

        slice.SliceEnd.Should().Be(At.AddHours(2));
    }

    [Fact]
    public void ComputeSlice_FocusBlockRitualCompletedAndNoActiveSessions_FocusSessionCompletedIsTrue()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        focusBlock.MarkRitualCompleted(At);

        var slice = _engine.ComputeSlice(At, [preset], [], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_FocusBlockRitualCompletedButSessionStillActive_FocusSessionCompletedIsFalse()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        focusBlock.MarkRitualCompleted(At);
        var session = CreateActiveSession();

        var slice = _engine.ComputeSlice(At, [preset], [session], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeFalse();
    }

    [Fact]
    public void ComputeSlice_FocusBlockRitualNotCompleted_FocusSessionCompletedIsFalse()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [preset], [], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeFalse();
    }

    [Fact]
    public void ComputeSlice_CancelledFocusBlock_ExcludedFromActiveBlockPresetType()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        focusBlock.MarkCancelled(At);

        var slice = _engine.ComputeSlice(At, [preset], [], [focusBlock]);

        slice.ActiveBlockPresetType.Should().BeNull();
        slice.ActiveFocusBlockId.Should().BeNull();
    }

    [Fact]
    public void ComputeSlice_CancelledBlock_ExcludedFromSliceEnd()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var cancelledBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(3));
        cancelledBlock.MarkCancelled(At);
        var liveBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));

        var slice = _engine.ComputeSlice(At, [preset], [], [cancelledBlock, liveBlock]);

        slice.SliceEnd.Should().Be(At.AddHours(1));
    }

    [Fact]
    public void ComputeSlice_OnlyBlockIsCancelled_SliceEndFallsBackToOneMinute()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var cancelledBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(3));
        cancelledBlock.MarkCancelled(At);

        var slice = _engine.ComputeSlice(At, [preset], [], [cancelledBlock]);

        slice.SliceEnd.Should().Be(At.AddMinutes(1));
    }

    [Fact]
    public void ComputeSlice_FocusBlockAlreadyEnded_ExcludedFromActiveFocusBlockId()
    {
        // ComputeActivePolicySliceQueryHandler's range query uses a ±1s buffer around "now"
        // (SQLite datetime comparison slop), so a block whose End fell in that buffer window
        // could still be handed to ComputeSlice even though it's genuinely past its own
        // scheduled end. PolicyEngine must be the final, precise authority — a block the
        // caller's loose query still returned must not become ActiveFocusBlockId (and
        // trigger the Ritual) once it's actually ended relative to `at`.
        var preset = CreatePreset(WebsiteFilterType.Blocklist, type: BlockPresetType.Focus);
        var endedBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.AddHours(-1), At.AddSeconds(-1));

        var slice = _engine.ComputeSlice(At, [preset], [], [endedBlock]);

        slice.ActiveFocusBlockId.Should().BeNull();
        slice.ActiveBlockPresetType.Should().BeNull();
    }

    [Fact]
    public void ComputeSlice_CancelledFocusBlockRitualCompleted_FocusSessionCompletedIsFalse()
    {
        // A cancelled block is excluded from consideration entirely, so it can no longer
        // drive FocusSessionCompleted true (there's no "focus block" left to look at).
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At, At.AddHours(1));
        focusBlock.MarkRitualCompleted(At);
        focusBlock.MarkCancelled(At);

        var slice = _engine.ComputeSlice(At, [preset], [], [focusBlock]);

        slice.FocusSessionCompleted.Should().BeFalse();
    }

    [Fact]
    public void ComputeSlice_ActiveSessionPresent_SetsActiveSessionId()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var session = CreateActiveSession();

        var slice = _engine.ComputeSlice(At, [preset], [session], []);

        slice.ActiveSessionId.Should().Be(session.Id);
    }

    [Fact]
    public void ComputeSlice_BlocklistWithNoBlockedAppsOrSites_IsEmpty()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);

        var slice = _engine.ComputeSlice(At, [preset], [], []);

        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_IsNeverEmpty()
    {
        var preset = CreatePreset(WebsiteFilterType.Allowlist);

        var slice = _engine.ComputeSlice(At, [preset], [], []);

        slice.IsEmpty.Should().BeFalse();
    }
}
