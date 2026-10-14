using FluentAssertions;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

/// <summary>
/// Updating a block preset, run as the app runs it: real pipeline, real SQLite (Gap 110). While a
/// running session or a future block uses the preset, it may only become at least as strict.
/// </summary>
public class UpdateBlockPresetTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    public UpdateBlockPresetTests() => Clock.SetNow(Now);

    private static BlockPreset Preset(string[]? apps = null, string[]? sites = null,
        WebsiteFilterType filterType = WebsiteFilterType.Blocklist)
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", filterType, BlockPresetType.Focus, At.Utc(Now));
        foreach (var app in apps ?? []) preset.AddBlockedApp(new BlockedApp(app));
        foreach (var site in sites ?? []) preset.AddWebsite(new Website(site));
        return preset;
    }

    private static UpdateBlockPresetCommand Command(BlockPreset preset) => new()
    {
        PresetId = preset.Id,
        Name = preset.Name,
        WebsiteFilterType = preset.WebsiteFilterType,
        PresetType = preset.PresetType,
        BlockedApps = preset.BlockedApps.Select(a => a.ProcessName).ToList(),
        Websites = preset.Websites.Select(s => s.Url).ToList()
    };

    private static Session RunningSessionUsing(BlockPreset preset)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            preset.Id, SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-5)));
        return session;
    }

    private static FocusBlock FutureBlockUsing(BlockPreset preset) =>
        FocusBlock.Create(Guid.NewGuid(), preset.Id, At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1)));

    private async Task<BlockPreset> StoredAsync(BlockPreset preset) => (await FindAsync<BlockPreset>(preset.Id))!;

    // ── Not in use: anything goes ──────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_ReplacesNameAppsAndSitesEntirely()
    {
        var preset = Preset(apps: ["spotify"], sites: ["reddit.com"]);
        await SeedAsync(preset);

        var result = await RunAsync(Command(preset) with { Name = "New Name", BlockedApps = ["chrome"], Websites = ["facebook.com"] });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await StoredAsync(preset);
        stored.Name.Should().Be("New Name");
        stored.BlockedApps.Select(a => a.ProcessName).Should().Equal("chrome");
        stored.Websites.Select(s => s.Url).Should().Equal("facebook.com");
    }

    [Fact]
    public async Task ChangingThePresetType_IsStored()
    {
        var preset = Preset(apps: ["chrome"]);
        await SeedAsync(preset);

        await RunAsync(Command(preset) with { PresetType = BlockPresetType.Background });

        (await StoredAsync(preset)).PresetType.Should().Be(BlockPresetType.Background);
    }

    [Fact]
    public async Task PresetNotFound_Fails()
    {
        var result = await RunAsync(Command(Preset(apps: ["chrome"])));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task NameAlreadyInUse_IsRefused_AndNothingChanges()
    {
        var preset = Preset(apps: ["spotify"]);
        var other = BlockPreset.Create(Guid.NewGuid(), "New Name", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now));
        await SeedAsync(preset, other);

        var result = await RunAsync(Command(preset) with { Name = "New Name", BlockedApps = ["chrome"] });

        result.Success.Should().BeFalse();
        (await StoredAsync(preset)).BlockedApps.Select(a => a.ProcessName).Should().Equal("spotify");
    }

    [Theory]
    [InlineData("   ", "chrome")]    // blank name
    [InlineData("New Name", "   ")]  // blank app name — only BlockedApp's own constructor rejects it
    public async Task ABlankNameOrApp_IsRefused_AndNothingChanges(string name, string app)
    {
        var preset = Preset(apps: ["spotify"]);
        await SeedAsync(preset);

        var result = await RunAsync(Command(preset) with { Name = name, BlockedApps = [app] });

        result.Success.Should().BeFalse();
        var stored = await StoredAsync(preset);
        stored.Name.Should().Be("Deep Work");
        stored.BlockedApps.Select(a => a.ProcessName).Should().Equal("spotify");
    }

    [Fact]
    public async Task AnUnkillableProcess_IsRefused()
    {
        var preset = Preset(apps: ["chrome"]);
        await SeedAsync(preset);

        var result = await RunAsync(Command(preset) with { BlockedApps = ["winlogon"] });

        result.Success.Should().BeFalse();
    }

    // ── In use: only at least as strict ────────────────────────────────────────────────

    [Fact]
    public async Task UsedByTheRunningSession_AddingAnApp_IsAllowed()
    {
        var preset = Preset(apps: ["chrome"]);
        await SeedAsync(preset, RunningSessionUsing(preset));

        var result = await RunAsync(Command(preset) with { BlockedApps = ["chrome", "firefox"] });

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await StoredAsync(preset)).BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(["chrome", "firefox"]);
    }

    [Fact]
    public async Task UsedByAFutureBlock_AddingASite_IsAllowed()
    {
        var preset = Preset(sites: ["reddit.com"]);
        await SeedAsync(preset, FutureBlockUsing(preset));

        var result = await RunAsync(Command(preset) with { Websites = ["reddit.com", "newsite.com"] });

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task InUse_AnAllowlistDroppingASite_IsAllowed()
    {
        // Under Allowlist, removing a site lets less through — the stricter direction.
        var preset = Preset(sites: ["allowed-a.com", "allowed-b.com"], filterType: WebsiteFilterType.Allowlist);
        await SeedAsync(preset, FutureBlockUsing(preset));

        var result = await RunAsync(Command(preset) with { Websites = ["allowed-a.com"] });

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    public static TheoryData<string> WeakerChanges => ["removing an app", "removing a site", "renaming", "changing the filter type", "changing the preset type"];

    private static UpdateBlockPresetCommand Weaken(UpdateBlockPresetCommand command, string change) => change switch
    {
        "removing an app" => command with { BlockedApps = ["chrome"] },
        "removing a site" => command with { Websites = [] },
        "renaming" => command with { Name = "New Name" },
        "changing the filter type" => command with { WebsiteFilterType = WebsiteFilterType.Allowlist },
        _ => command with { PresetType = BlockPresetType.Background },
    };

    [Theory]
    [MemberData(nameof(WeakerChanges))]
    public async Task InUse_AnythingButStricter_IsRefused_AndNothingChanges(string change)
    {
        var preset = Preset(apps: ["chrome", "firefox"], sites: ["reddit.com"]);
        await SeedAsync(preset, RunningSessionUsing(preset));

        var result = await RunAsync(Weaken(Command(preset), change));

        result.Success.Should().BeFalse(change);
        var stored = await StoredAsync(preset);
        stored.Name.Should().Be("Deep Work");
        stored.PresetType.Should().Be(BlockPresetType.Focus);
        stored.BlockedApps.Should().HaveCount(2);
        stored.Websites.Should().HaveCount(1);
    }

    [Fact]
    public async Task InUse_AnAllowlistAddingASite_IsRefused()
    {
        // Adding to an Allowlist lets more through — weaker, though it is an addition.
        var preset = Preset(sites: ["allowed-a.com"], filterType: WebsiteFilterType.Allowlist);
        await SeedAsync(preset, RunningSessionUsing(preset));

        var result = await RunAsync(Command(preset) with { Websites = ["allowed-a.com", "allowed-b.com"] });

        result.Success.Should().BeFalse();
    }
}
