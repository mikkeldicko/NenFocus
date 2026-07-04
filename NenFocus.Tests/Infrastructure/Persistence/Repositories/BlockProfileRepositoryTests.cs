using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

public class BlockProfileRepositoryTests : SqliteRepositoryTestBase
{
    private BlockProfileRepository CreateRepository() => new(Context);

    [Fact]
    public async Task AddAsync_ThenSaveChanges_IsRetrievableWithAppsAndSites()
    {
        var repo = CreateRepository();
        var profile = BlockProfile.Create(Guid.NewGuid(), "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);
        profile.AddBlockedApp(new BlockedApp("chrome"));
        profile.AddWebsite(new BlockedSite("facebook.com"));

        await repo.AddAsync(profile);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(profile.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Deep Work");
        reloaded.BlockedApps.Select(a => a.ProcessName).Should().Contain("chrome");
        reloaded.Websites.Select(s => s.Url).Should().Contain("facebook.com");
    }

    [Fact]
    public async Task GetAllAsync_OrdersByName()
    {
        var repo = CreateRepository();
        await repo.AddAsync(BlockProfile.Create(Guid.NewGuid(), "Zeta", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        await repo.AddAsync(BlockProfile.Create(Guid.NewGuid(), "Alpha", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        await Context.SaveChangesAsync();

        var all = await CreateRepository().GetAllAsync();

        var list = all.ToList();
        list.FindIndex(p => p.Name == "Alpha").Should().BeLessThan(list.FindIndex(p => p.Name == "Zeta"));
    }

    [Fact]
    public async Task DeleteAsync_ThenSaveChanges_RemovesProfile()
    {
        var repo = CreateRepository();
        var profile = BlockProfile.Create(Guid.NewGuid(), "Temp Profile", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);
        await repo.AddAsync(profile);
        await Context.SaveChangesAsync();

        await CreateRepository().DeleteAsync(profile.Id);
        await Context.SaveChangesAsync();

        (await CreateRepository().GetByIdAsync(profile.Id)).Should().BeNull();
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
    [InlineData("focus — allowlist")]
    [InlineData("FOCUS — ALLOWLIST")]
    [InlineData("Focus — Allowlist")]
    public async Task ExistsWithNameAsync_IsCaseInsensitive(string candidate)
    {
        var exists = await CreateRepository().ExistsWithNameAsync(candidate);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsWithNameAsync_ExcludingSelf_ReturnsFalseForOwnName()
    {
        var repo = CreateRepository();
        var profile = BlockProfile.Create(Guid.NewGuid(), "Unique Name", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);
        await repo.AddAsync(profile);
        await Context.SaveChangesAsync();

        var exists = await CreateRepository().ExistsWithNameAsync("Unique Name", excludeId: profile.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_MutatingTrackedEntity_PersistsOnSaveChangesEvenThoughUpdateAsyncIsANoOp()
    {
        // BlockProfileRepository.UpdateAsync() is intentionally a no-op — the entity is
        // already tracked by this same context, so EF Core's change tracker picks up the
        // mutation automatically on SaveChangesAsync. This test pins down that the no-op
        // actually works as intended rather than silently dropping edits.
        var repo = CreateRepository();
        var profile = BlockProfile.Create(Guid.NewGuid(), "Original", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);
        await repo.AddAsync(profile);
        await Context.SaveChangesAsync();

        var tracked = await repo.GetByIdAsync(profile.Id);
        tracked!.Rename("Renamed");
        tracked.AddBlockedApp(new BlockedApp("spotify"));
        await repo.UpdateAsync(tracked);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(profile.Id);
        reloaded!.Name.Should().Be("Renamed");
        reloaded.BlockedApps.Select(a => a.ProcessName).Should().Contain("spotify");
    }

    [Fact]
    public async Task Tags_SurviveAReloadFromTheDatabase()
    {
        // BlockProfile.Tags has no explicit EF Core conversion configured in
        // OnModelCreating — it relies entirely on EF Core's automatic primitive-collection
        // convention discovering the private _tags backing field. Worth pinning down with
        // a real round-trip since it isn't obvious from reading the mapping code alone.
        var repo = CreateRepository();
        var profile = BlockProfile.Create(
            Guid.NewGuid(), "Tagged Profile", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow, tags: ["work", "focus"]);
        await repo.AddAsync(profile);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(profile.Id);

        reloaded!.Tags.Should().BeEquivalentTo(["work", "focus"]);
    }

    [Fact]
    public async Task GetByTagsAsync_MatchesProfilesTaggedWithGivenTag()
    {
        var repo = CreateRepository();
        var profile = BlockProfile.Create(
            Guid.NewGuid(), "Tagged Profile 2", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow, tags: ["work"]);
        await repo.AddAsync(profile);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetByTagsAsync(["work"]);

        result.Should().ContainSingle(p => p.Id == profile.Id);
    }
}
