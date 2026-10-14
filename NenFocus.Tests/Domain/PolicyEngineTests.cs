using FluentAssertions;
using TestAt = NenFocus.Tests.At;
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
        var preset = BlockPreset.Create(Guid.NewGuid(), "Preset", policy, type, TestAt.Utc(At));
        foreach (var app in apps ?? [])
            preset.AddBlockedApp(new BlockedApp(app));
        foreach (var site in sites ?? [])
            preset.AddWebsite(new Website(site));
        return preset;
    }

    private static Session CreateActiveSession(
        Guid? blockPresetId = null,
        IReadOnlyList<string>? temporaryBlockedApps = null,
        IReadOnlyList<string>? temporaryBlockedSites = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            blockPresetId ?? Guid.NewGuid(),
            SessionOrigin.Spontaneous,
            temporaryBlockedApps: temporaryBlockedApps?.Select(a => new BlockedApp(a)).ToList(),
            temporaryBlockedSites: temporaryBlockedSites?.Select(s => new Website(s)).ToList());
        session.Start(TestAt.Utc(At));
        return session;
    }

    private static IEnumerable<string> Names(ActivePolicySlice slice) => slice.BlockedApps.Select(a => a.ProcessName);
    private static IEnumerable<string> Urls(ActivePolicySlice slice) => slice.BlockedSites.Select(s => s.Url);
    private static bool Blocks(ActivePolicySlice slice, string site) => slice.Blocks(new Website(site));

    // A preset only contributes to BlockedApps/BlockedSites/AllowLists when a currently
    // active block or session actually references it (see PolicyEngine.ComputeSlice) — so
    // any test asserting on those needs a block/session wired to the preset's Id, not just
    // the preset passed in on its own.
    private static FocusBlock CreateActiveBlock(Guid blockPresetId)
        => FocusBlock.Create(Guid.NewGuid(), blockPresetId,TestAt.Slot(At, At.AddHours(1)));

    [Fact]
    public void ComputeSlice_NoPresetsAndNoSessions_ReturnsMinimalEmptySlice()
    {
        var slice = _engine.ComputeSlice(TestAt.Utc(At), [], null, []);

        slice.BlockedApps.Should().BeEmpty();
        slice.BlockedSites.Should().BeEmpty();
        slice.ActiveSessionId.Should().BeNull();
        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_BlockWithNoResolvablePreset_BlocksNothing()
    {
        // An orphaned block (its preset could not be resolved, e.g. deleted out from under
        // it) has no preset data to contribute — and must not throw. (Its Ritual trigger
        // doesn't depend on the slice at all — see GetFocusBlockAwaitingRitualQuery, Gap 62.)
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(At, At.AddHours(1)));

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [], null, [block]);

        slice.BlockedApps.Should().BeEmpty();
        slice.BlockedSites.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_UnionsBlockedAppsAcrossPresetsAndNormalizesNames()
    {
        var presetA = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var presetB = CreatePreset(WebsiteFilterType.Blocklist, apps: ["Spotify"]);
        var blockA = CreateActiveBlock(presetA.Id);
        var blockB = CreateActiveBlock(presetB.Id);
        var session = CreateActiveSession(temporaryBlockedApps: ["notepad.exe"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [presetA, presetB], session, [blockA, blockB]);

        Names(slice).Should().BeEquivalentTo(["chrome", "spotify", "notepad"]);
    }

    [Fact]
    public void ComputeSlice_BlockedAppDedupeIsCaseInsensitive()
    {
        var presetA = CreatePreset(WebsiteFilterType.Blocklist, apps: ["Chrome"]);
        var presetB = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var blockA = CreateActiveBlock(presetA.Id);
        var blockB = CreateActiveBlock(presetB.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [presetA, presetB], null, [blockA, blockB]);

        Names(slice).Should().ContainSingle().Which.Should().Be("chrome");
    }

    [Fact]
    public void ComputeSlice_BlocklistMode_UnionsSitesAcrossPresets()
    {
        var presetA = CreatePreset(WebsiteFilterType.Blocklist, sites: ["facebook.com"]);
        var presetB = CreatePreset(WebsiteFilterType.Blocklist, sites: ["youtube.com"]);
        var blockA = CreateActiveBlock(presetA.Id);
        var blockB = CreateActiveBlock(presetB.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [presetA, presetB], null, [blockA, blockB]);

        slice.WebsiteFilterTypeMode.Should().Be(WebsiteFilterType.Blocklist);
        Urls(slice).Should().BeEquivalentTo(["facebook.com", "youtube.com"]);
        slice.AllowLists.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_BlocklistMode_IncludesTemporaryBlockedSites()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["facebook.com"]);
        var block = CreateActiveBlock(preset.Id);
        var session = CreateActiveSession(temporaryBlockedSites: ["reddit.com"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], session, [block]);

        Urls(slice).Should().BeEquivalentTo(["facebook.com", "reddit.com"]);
    }

    [Fact]
    public void ComputeSlice_AnyAllowlistPreset_SwitchesEffectivePolicyToAllowlist()
    {
        var preset = CreatePreset(WebsiteFilterType.Allowlist, sites: ["work.com"]);
        var block = CreateActiveBlock(preset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [block]);

        slice.WebsiteFilterTypeMode.Should().Be(WebsiteFilterType.Allowlist);
        Blocks(slice, "work.com").Should().BeFalse();
        Blocks(slice, "anything-else.com").Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_MultipleAllowlistPresets_OnlyWhatEveryOneAllowsGetsThrough()
    {
        var presetA = CreatePreset(WebsiteFilterType.Allowlist, sites: ["a.com", "b.com"]);
        var presetB = CreatePreset(WebsiteFilterType.Allowlist, sites: ["b.com", "c.com"]);
        var blockA = CreateActiveBlock(presetA.Id);
        var blockB = CreateActiveBlock(presetB.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [presetA, presetB], null, [blockA, blockB]);

        Blocks(slice, "b.com").Should().BeFalse();
        Blocks(slice, "a.com").Should().BeTrue();
        Blocks(slice, "c.com").Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_ABlocklistPresetBlocksWhatAnAllowlistAllows()
    {
        var allowPreset = CreatePreset(WebsiteFilterType.Allowlist, sites: ["a.com", "b.com"]);
        var blockPreset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["b.com"]);
        var allowBlock = CreateActiveBlock(allowPreset.Id);
        var blockBlock = CreateActiveBlock(blockPreset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [allowPreset, blockPreset], null, [allowBlock, blockBlock]);

        Blocks(slice, "a.com").Should().BeFalse();
        Blocks(slice, "b.com").Should().BeTrue();
        Urls(slice).Should().BeEquivalentTo(["b.com"]); // the hosts file writes these
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_TemporaryBlockedSiteOverridesAllowlistMembership()
    {
        var allowPreset = CreatePreset(WebsiteFilterType.Allowlist, sites: ["a.com", "temp.com"]);
        var session = CreateActiveSession(blockPresetId: allowPreset.Id, temporaryBlockedSites: ["temp.com"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [allowPreset], session, []);

        Blocks(slice, "a.com").Should().BeFalse();
        Blocks(slice, "temp.com").Should().BeTrue();
        Urls(slice).Should().BeEquivalentTo(["temp.com"]);
    }

    // Gap 112 (C1): a site covers its subdomains, so allowlists are kept apart and compared by
    // coverage — the old precomputed intersection compared exact strings.
    [Fact]
    public void ComputeSlice_BlocklistMode_ASiteBlocksItsSubdomains()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["youtube.com"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [CreateActiveBlock(preset.Id)]);

        Blocks(slice, "m.youtube.com").Should().BeTrue();
        Blocks(slice, "notyoutube.com").Should().BeFalse();
    }

    [Fact]
    public void ComputeSlice_TwoAllowlists_ASubdomainBothCoverGetsThrough()
    {
        var allowGoogle = CreatePreset(WebsiteFilterType.Allowlist, sites: ["google.com"]);
        var allowGmail = CreatePreset(WebsiteFilterType.Allowlist, sites: ["mail.google.com"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [allowGoogle, allowGmail], null,
            [CreateActiveBlock(allowGoogle.Id), CreateActiveBlock(allowGmail.Id)]);

        Blocks(slice, "mail.google.com").Should().BeFalse();
        Blocks(slice, "docs.google.com").Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_AllowedDomainWithABlockedSubdomain_TheSubdomainStaysBlocked()
    {
        var allowGoogle = CreatePreset(WebsiteFilterType.Allowlist, sites: ["google.com"]);
        var blockGmail = CreatePreset(WebsiteFilterType.Blocklist, sites: ["mail.google.com"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [allowGoogle, blockGmail], null,
            [CreateActiveBlock(allowGoogle.Id), CreateActiveBlock(blockGmail.Id)]);

        Blocks(slice, "mail.google.com").Should().BeTrue();
        Blocks(slice, "docs.google.com").Should().BeFalse();
    }

    [Fact]
    public void BlockedAppFor_FindsTheListedAppARunningProcessIs()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist, apps: [@"C:\Games\Steam.exe"]);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [CreateActiveBlock(preset.Id)]);

        slice.BlockedAppFor("Steam").Should().Be(new BlockedApp("steam"));
        slice.BlockedAppFor("explorer").Should().BeNull();
    }

    [Fact]
    public void ComputeSlice_CancelledBlockHandedIn_BlocksNothing()
    {
        // Regression guard: no caller has to filter cancelled blocks first — the block's own
        // IsActive already says no (cancellation shrinks its EffectiveEnd).
        var preset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["youtube.com"]);
        var cancelledBlock = CreateActiveBlock(preset.Id);
        cancelledBlock.MarkCancelled(TestAt.Utc(At.AddMinutes(-1)));

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [cancelledBlock]);

        slice.IsEmpty.Should().BeTrue();
    }


    [Fact]
    public void ComputeSlice_FocusBlockAlreadyEnded_ItsPresetIsNotEnforced()
    {
        // ComputeActivePolicySliceQueryHandler's range query uses a ±1s buffer around "now"
        // (SQLite datetime comparison slop), so a block whose End fell in that buffer window
        // could still be handed to ComputeSlice even though it's genuinely past its own
        // scheduled end. PolicyEngine must be the final, precise authority.
        var preset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["youtube.com"]);
        var endedBlock = FocusBlock.Create(Guid.NewGuid(), preset.Id,TestAt.Slot(At.AddHours(-1).AddSeconds(-1), At.AddSeconds(-1)));

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [endedBlock]);

        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_FocusBlockSessionEndedEarly_PresetIsNoLongerActive()
    {
        // Reproduces a live bug: a FocusBlock's session ending early correctly dropped it
        // from activeBlocks (via EffectiveEnd), but its preset kept getting pulled from the
        // caller's over-fetched `presets` list regardless, so BlockedSites/BlockedApps kept
        // reapplying every heartbeat tick for as long as `at` was still inside the block's
        // *original* Start/End window — enforcement never actually stopped. Once the block
        // has dropped out of activeBlocks entirely, the slice is simply empty, same as if
        // there were no block at all.
        var preset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["facebook.com"]);
        var endedEarlyBlock = FocusBlock.Create(Guid.NewGuid(), preset.Id,TestAt.Slot(At.AddHours(-1), At.AddHours(1)));
        endedEarlyBlock.MarkRitualCompleted(TestAt.Utc(At.AddHours(-1)));
        endedEarlyBlock.MarkSessionEnded(TestAt.Utc(At.AddMinutes(-2))); // ended early, well before its raw End

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [endedEarlyBlock]);

        slice.BlockedSites.Should().BeEmpty();
        slice.BlockedApps.Should().BeEmpty();
        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_FocusBlockSessionEndedEarly_CoOccurringBackgroundBlockStaysBlocked()
    {
        // Gap 62: FocusBlock.EffectiveEnd alone must drop the finished block's preset, while
        // a Background Block running alongside keeps enforcing — no special-case flag needed.
        var focusPreset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["youtube.com"]);
        var backgroundPreset = CreatePreset(WebsiteFilterType.Blocklist, sites: ["reddit.com"], type: BlockPresetType.Background);
        var endedEarlyBlock = FocusBlock.Create(Guid.NewGuid(), focusPreset.Id,TestAt.Slot(At.AddHours(-1), At.AddHours(1)));
        endedEarlyBlock.MarkRitualCompleted(TestAt.Utc(At.AddHours(-1)));
        endedEarlyBlock.MarkSessionEnded(TestAt.Utc(At.AddMinutes(-2)));
        var backgroundBlock = BackgroundBlock.Create(Guid.NewGuid(), backgroundPreset.Id,TestAt.Slot(At.AddHours(-1), At.AddHours(1)));

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [focusPreset, backgroundPreset], null, [endedEarlyBlock, backgroundBlock]);

        Urls(slice).Should().BeEquivalentTo(["reddit.com"]);
    }

    [Fact]
    public void ComputeSlice_ActiveSessionPresent_SetsActiveSessionId()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var session = CreateActiveSession();

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], session, []);

        slice.ActiveSessionId.Should().Be(session.Id);
    }

    [Fact]
    public void ComputeSlice_BlocklistWithNoBlockedAppsOrSites_IsEmpty()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        var block = CreateActiveBlock(preset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [block]);

        slice.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlice_AllowlistMode_IsNeverEmpty()
    {
        var preset = CreatePreset(WebsiteFilterType.Allowlist);
        var block = CreateActiveBlock(preset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], null, [block]);

        slice.IsEmpty.Should().BeFalse();
    }

    // A planned session that started 26 minutes before `At`, with a 25-30 minute break
    // slot — so `At` itself falls inside the break (Gap 63: a break is read off the
    // schedule against the clock, never a status the session is put into).
    private static Session StartPlannedSessionInsideBreakAt(FocusBlock block, Guid presetId, bool keepBlockingDuringBreaks = false)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Focus"), new SessionDuration(TimeSpan.FromHours(1)),
            presetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(TestAt.Utc(At.AddMinutes(-26)), block.Scheduled, new BreakSchedule([
            new SessionBreak(SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30))]),
            keepBlockingDuringBreaks: keepBlockingDuringBreaks);
        return session;
    }

    [Fact]
    public void ComputeSlice_PlannedSessionInBreakSlot_PresetExcludedFromBlockedApps()
    {
        // By default a break lifts the session's own preset (and its originating block's).
        var preset = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var block = FocusBlock.Create(Guid.NewGuid(), preset.Id,TestAt.Slot(At.AddMinutes(-26), At.AddHours(1)));
        var session = StartPlannedSessionInsideBreakAt(block, preset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], session, [block]);

        slice.BlockedApps.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlice_SameSessionOnceBreakSlotEnds_PresetBlockedAgain_WithNoStateChange()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var block = FocusBlock.Create(Guid.NewGuid(), preset.Id,TestAt.Slot(At.AddMinutes(-26), At.AddHours(1)));
        var session = StartPlannedSessionInsideBreakAt(block, preset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At.AddMinutes(5)), [preset], session, [block]);

        Names(slice).Should().BeEquivalentTo(["chrome"]);
    }

    [Fact]
    public void ComputeSlice_SessionKeepingBlockingDuringBreaks_PresetStaysBlockedInsideItsBreakSlot()
    {
        // Gap 64: the session carries its contract's rule — PolicyEngine needs no contract.
        var preset = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var block = FocusBlock.Create(Guid.NewGuid(), preset.Id,TestAt.Slot(At.AddMinutes(-26), At.AddHours(1)));
        var session = StartPlannedSessionInsideBreakAt(block, preset.Id, keepBlockingDuringBreaks: true);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [preset], session, [block]);

        Names(slice).Should().BeEquivalentTo(["chrome"]);
    }

    [Fact]
    public void ComputeSlice_SessionInBreakSlot_UnrelatedBackgroundBlockStaysBlocked()
    {
        // Pausing only ever affects the specific block/session whose break is in
        // progress — an unrelated, merely co-occurring Background Block must never be
        // swept into the same exclusion.
        var focusPreset = CreatePreset(WebsiteFilterType.Blocklist, apps: ["chrome.exe"]);
        var backgroundPreset = CreatePreset(WebsiteFilterType.Blocklist, apps: ["spotify.exe"], type: BlockPresetType.Background);
        var focusBlock = FocusBlock.Create(Guid.NewGuid(), focusPreset.Id,TestAt.Slot(At.AddMinutes(-26), At.AddHours(1)));
        var backgroundBlock = BackgroundBlock.Create(Guid.NewGuid(), backgroundPreset.Id,TestAt.Slot(At, At.AddHours(1)));
        var session = StartPlannedSessionInsideBreakAt(focusBlock, focusPreset.Id);

        var slice = _engine.ComputeSlice(TestAt.Utc(At), [focusPreset, backgroundPreset], session, [focusBlock, backgroundBlock]);

        Names(slice).Should().BeEquivalentTo(["spotify"]);
    }
}
