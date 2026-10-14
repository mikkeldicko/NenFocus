using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

/// <summary>
/// The block preset collection against real SQLite. Every read-back goes through a fresh context, so
/// it sees the database, never the change tracker that saved (Gap 110: a same-context reload hid
/// that tags were never stored). Changes to a loaded preset: <c>ChangeTrackingRoundTripTests</c>.
/// </summary>
public class BlockPresetRepositoryTests : SqliteRepositoryTestBase
{
    private static BlockPreset Preset(string name) =>
        BlockPreset.Create(Guid.NewGuid(), name, WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(2026, 1, 1));

    private BlockPresetRepository Fresh() => new(CreateFreshContext());

    private async Task AddAndCommitAsync(params BlockPreset[] presets)
    {
        var repository = new BlockPresetRepository(Context);
        foreach (var preset in presets) repository.Add(preset);
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Add_ThenCommit_IsStoredWithItsAppsAndSites()
    {
        var preset = Preset("Deep Work");
        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddWebsite(new Website("facebook.com"));

        await AddAndCommitAsync(preset);

        var stored = await Fresh().GetByIdAsync(preset.Id);
        stored!.Name.Should().Be("Deep Work");
        stored.BlockedApps.Select(a => a.ProcessName).Should().Equal("chrome");
        stored.Websites.Select(s => s.Url).Should().Equal("facebook.com");
    }

    [Fact]
    public async Task GetAllAsync_OrdersByName()
    {
        await AddAndCommitAsync(Preset("Zeta"), Preset("Alpha"));

        var all = (await Fresh().GetAllAsync()).ToList();

        all.FindIndex(p => p.Name == "Alpha").Should().BeLessThan(all.FindIndex(p => p.Name == "Zeta"));
    }

    [Fact]
    public async Task Remove_ThenCommit_IsGone()
    {
        var preset = Preset("Temp Preset");
        await AddAndCommitAsync(preset);

        await using (var scope = CreateFreshContext())
        {
            var repository = new BlockPresetRepository(scope);
            repository.Remove((await repository.GetByIdAsync(preset.Id))!);
            await scope.SaveChangesAsync();
        }

        (await Fresh().GetByIdAsync(preset.Id)).Should().BeNull();
    }

    [Theory]
    [InlineData("ai & dev tools only")]
    [InlineData("AI & DEV TOOLS ONLY")]
    [InlineData("AI & Dev Tools Only")]
    public async Task ExistsWithNameAsync_IsCaseInsensitive(string candidate)
    {
        (await Fresh().ExistsWithNameAsync(candidate, BlockPresetType.Focus)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsWithNameAsync_DifferentPresetType_ReturnsFalse()
    {
        // "AI & Dev Tools Only" is seeded as a Focus preset — a Background preset asking
        // about the same name is a different scope entirely, not a collision.
        (await Fresh().ExistsWithNameAsync("AI & Dev Tools Only", BlockPresetType.Background)).Should().BeFalse();
    }

    [Fact]
    public async Task ExistsWithNameAsync_ExcludingSelf_ReturnsFalseForOwnName()
    {
        var preset = Preset("Unique Name");
        await AddAndCommitAsync(preset);

        (await Fresh().ExistsWithNameAsync("Unique Name", BlockPresetType.Focus, excludeId: preset.Id)).Should().BeFalse();
    }
}
