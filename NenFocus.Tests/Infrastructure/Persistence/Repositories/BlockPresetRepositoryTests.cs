using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

public class BlockPresetRepositoryTests : SqliteRepositoryTestBase
{
    private BlockPresetRepository CreateRepository() => new(Context);

    [Fact]
    public async Task AddAsync_ThenSaveChanges_IsRetrievableWithAppsAndSites()
    {
        var repo = CreateRepository();
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddWebsite(new BlockedSite("facebook.com"));

        await repo.AddAsync(preset);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(preset.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Deep Work");
        reloaded.BlockedApps.Select(a => a.ProcessName).Should().Contain("chrome");
        reloaded.Websites.Select(s => s.Url).Should().Contain("facebook.com");
    }

    [Fact]
    public async Task GetAllAsync_OrdersByName()
    {
        var repo = CreateRepository();
        await repo.AddAsync(BlockPreset.Create(Guid.NewGuid(), "Zeta", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        await repo.AddAsync(BlockPreset.Create(Guid.NewGuid(), "Alpha", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        await Context.SaveChangesAsync();

        var all = await CreateRepository().GetAllAsync();

        var list = all.ToList();
        list.FindIndex(p => p.Name == "Alpha").Should().BeLessThan(list.FindIndex(p => p.Name == "Zeta"));
    }

    [Fact]
    public async Task DeleteAsync_ThenSaveChanges_RemovesPreset()
    {
        var repo = CreateRepository();
        var preset = BlockPreset.Create(Guid.NewGuid(), "Temp Preset", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        await repo.AddAsync(preset);
        await Context.SaveChangesAsync();

        await CreateRepository().DeleteAsync(preset.Id);
        await Context.SaveChangesAsync();

        (await CreateRepository().GetByIdAsync(preset.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        var act = async () =>
        {
            await CreateRepository().DeleteAsync(Guid.NewGuid());
            await Context.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("ai & dev tools only")]
    [InlineData("AI & DEV TOOLS ONLY")]
    [InlineData("AI & Dev Tools Only")]
    public async Task ExistsWithNameAsync_IsCaseInsensitive(string candidate)
    {
        var exists = await CreateRepository().ExistsWithNameAsync(candidate, BlockPresetType.Focus);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsWithNameAsync_DifferentPresetType_ReturnsFalse()
    {
        // "AI & Dev Tools Only" is seeded as a Focus preset — a Background preset asking
        // about the same name is a different scope entirely, not a collision.
        var exists = await CreateRepository().ExistsWithNameAsync("AI & Dev Tools Only", BlockPresetType.Background);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsWithNameAsync_ExcludingSelf_ReturnsFalseForOwnName()
    {
        var repo = CreateRepository();
        var preset = BlockPreset.Create(Guid.NewGuid(), "Unique Name", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        await repo.AddAsync(preset);
        await Context.SaveChangesAsync();

        var exists = await CreateRepository().ExistsWithNameAsync("Unique Name", BlockPresetType.Focus, excludeId: preset.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_MutatingTrackedEntity_PersistsOnSaveChangesEvenThoughUpdateAsyncIsANoOp()
    {
        // BlockPresetRepository.UpdateAsync() is intentionally a no-op — the entity is
        // already tracked by this same context, so EF Core's change tracker picks up the
        // mutation automatically on SaveChangesAsync. This test pins down that the no-op
        // actually works as intended rather than silently dropping edits.
        var repo = CreateRepository();
        var preset = BlockPreset.Create(Guid.NewGuid(), "Original", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        await repo.AddAsync(preset);
        await Context.SaveChangesAsync();

        var tracked = await repo.GetByIdAsync(preset.Id);
        tracked!.Rename("Renamed");
        tracked.AddBlockedApp(new BlockedApp("spotify"));
        await repo.UpdateAsync(tracked);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(preset.Id);
        reloaded!.Name.Should().Be("Renamed");
        reloaded.BlockedApps.Select(a => a.ProcessName).Should().Contain("spotify");
    }

    [Fact]
    public async Task Tags_SurviveAReloadFromTheDatabase()
    {
        // BlockPreset.Tags has no explicit EF Core conversion configured in
        // OnModelCreating — it relies entirely on EF Core's automatic primitive-collection
        // convention discovering the private _tags backing field. Worth pinning down with
        // a real round-trip since it isn't obvious from reading the mapping code alone.
        var repo = CreateRepository();
        var preset = BlockPreset.Create(
            Guid.NewGuid(), "Tagged Preset", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow, tags: ["work", "focus"]);
        await repo.AddAsync(preset);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(preset.Id);

        reloaded!.Tags.Should().BeEquivalentTo(["work", "focus"]);
    }

    [Fact]
    public async Task GetByTagsAsync_MatchesPresetsTaggedWithGivenTag()
    {
        var repo = CreateRepository();
        var preset = BlockPreset.Create(
            Guid.NewGuid(), "Tagged Preset 2", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow, tags: ["work"]);
        await repo.AddAsync(preset);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetByTagsAsync(["work"]);

        result.Should().ContainSingle(p => p.Id == preset.Id);
    }
}
