using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

/// <summary>Creating a block preset, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class CreateBlockPresetTests : UseCaseTestBase
{
    public CreateBlockPresetTests() => Clock.SetNow(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    private static CreateBlockPresetCommand ValidCommand() => new()
    {
        Name = "Deep Work",
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        PresetType = BlockPresetType.Focus,
        BlockedApps = ["chrome"],
        Websites = ["facebook.com"]
    };

    private Task<int> CountNamedAsync(string name) =>
        ReadAsync(context => context.BlockPresets.CountAsync(p => p.Name == name));

    [Fact]
    public async Task HappyPath_StoresThePresetWithItsAppsAndSites()
    {
        var result = await RunAsync(ValidCommand());

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await FindAsync<BlockPreset>(result.PresetId!.Value);
        stored!.Name.Should().Be("Deep Work");
        stored.BlockedApps.Select(a => a.ProcessName).Should().Equal("chrome");
        stored.Websites.Select(s => s.Url).Should().Equal("facebook.com");
    }

    [Fact]
    public async Task NameAlreadyInUse_IsRefused_AndNothingIsStored()
    {
        await SeedAsync(BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(2026, 1, 1)));

        var result = await RunAsync(ValidCommand());

        result.Success.Should().BeFalse();
        (await CountNamedAsync("Deep Work")).Should().Be(1);
    }

    [Fact]
    public async Task EmptyName_IsRefused_WithTheRequiredMessage()
    {
        var result = await RunAsync(ValidCommand() with { Name = "" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Preset name is required.");
    }

    [Fact]
    public async Task NoAppsAndNoWebsites_IsRefused()
    {
        var result = await RunAsync(ValidCommand() with { BlockedApps = [], Websites = [] });

        result.Success.Should().BeFalse();
        (await CountNamedAsync("Deep Work")).Should().Be(0);
    }

    [Fact]
    public async Task AnUnkillableProcess_IsRefused()
    {
        var result = await RunAsync(ValidCommand() with { BlockedApps = ["explorer.exe"] });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("'explorer.exe' is a critical system process and cannot be blocked.");
        (await CountNamedAsync("Deep Work")).Should().Be(0);
    }

    [Fact]
    public async Task AppsAndSitesAsTyped_AreStoredAsTheAppAndSiteTheyAre()
    {
        // Gap 112: Website and BlockedApp are the one definition of a site and an app.
        var result = await RunAsync(ValidCommand() with
        {
            BlockedApps = [@"C:\Program Files\Steam\Steam.exe"],
            Websites = ["https://www.YouTube.com/watch?v=1"]
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await FindAsync<BlockPreset>(result.PresetId!.Value);
        stored!.BlockedApps.Select(a => a.ProcessName).Should().Equal("steam");
        stored.Websites.Select(s => s.Url).Should().Equal("youtube.com");
    }

    [Fact]
    public async Task ASiteThatIsNoWebsite_IsRefused_SayingWhy()
    {
        var result = await RunAsync(ValidCommand() with { Websites = ["you tube.com"] });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("'you tube.com' is not a website — enter a domain like youtube.com.");
        (await CountNamedAsync("Deep Work")).Should().Be(0);
    }

    [Fact]
    public async Task ABlankAppName_IsRefusedCleanly_InsteadOfThrowing()
    {
        // The validator only checks the lists aren't empty; BlockedApp's own constructor rejects a blank name.
        var result = await RunAsync(ValidCommand() with { BlockedApps = ["   "] });

        result.Success.Should().BeFalse();
        (await CountNamedAsync("Deep Work")).Should().Be(0);
    }
}
