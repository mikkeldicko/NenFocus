using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

/// <summary>Duplicating a block preset, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class DuplicateBlockPresetTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    public DuplicateBlockPresetTests() => Clock.SetNow(Now);

    private static BlockPreset Preset(string name)
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), name, WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now));
        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddWebsite(new BlockedSite("youtube.com"));
        return preset;
    }

    private Task<DuplicateBlockPresetResult> DuplicateAsync(Guid sourceId, string? newName = null) =>
        RunAsync(new DuplicateBlockPresetCommand { SourcePresetId = sourceId, NewName = newName });

    private Task<int> PresetCountAsync() => ReadAsync(context => context.BlockPresets.CountAsync());

    [Fact]
    public async Task SourceNotFound_Fails()
    {
        var result = await DuplicateAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task WithANewName_StoresACopyUnderThatName_AppsAndSitesIncluded()
    {
        var source = Preset("Deep Work");
        await SeedAsync(source);

        var result = await DuplicateAsync(source.Id, "Weekend Mode");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.NewPresetId.Should().NotBe(source.Id);
        var copy = await FindAsync<BlockPreset>(result.NewPresetId!.Value);
        copy!.Name.Should().Be("Weekend Mode");
        copy.BlockedApps.Select(a => a.ProcessName).Should().Equal("chrome");
        copy.Websites.Select(s => s.Url).Should().Equal("youtube.com");
    }

    [Fact]
    public async Task WithoutANewName_IsNamedCopyOfTheSource()
    {
        var source = Preset("Deep Work");
        await SeedAsync(source);

        var result = await DuplicateAsync(source.Id);

        (await FindAsync<BlockPreset>(result.NewPresetId!.Value))!.Name.Should().Be("Copy of Deep Work");
    }

    [Fact]
    public async Task NameCollisions_NumberTheCopy_UntilTheNameIsFree()
    {
        var source = Preset("Deep Work");
        await SeedAsync(source, Preset("Copy of Deep Work"), Preset("Copy of Deep Work (2)"));

        var result = await DuplicateAsync(source.Id);

        (await FindAsync<BlockPreset>(result.NewPresetId!.Value))!.Name.Should().Be("Copy of Deep Work (3)");
    }

    [Fact]
    public async Task AnExplicitBlankName_IsRefusedCleanly_InsteadOfThrowing()
    {
        // An explicit "   " (not null) skips the default name and reaches BlockPreset's own check.
        var source = Preset("Deep Work");
        await SeedAsync(source);
        var before = await PresetCountAsync();

        var result = await DuplicateAsync(source.Id, "   ");

        result.Success.Should().BeFalse();
        (await PresetCountAsync()).Should().Be(before);
    }
}
