using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 112 (I2, I5): Website and BlockedApp are the one definition of a site and an app, and the
/// compiler holds every route in but one — a row EF reads back. If EF filled the value without its
/// constructor, a row written before Gap 112 (or by hand) would come back un-normalized. This pins that
/// it goes through the constructor.
/// </summary>
public class StoredSiteAndAppParityTests : SqliteRepositoryTestBase
{
    [Fact]
    public async Task AMessyPresetRow_ComesBackAsTheSiteAndAppItIs()
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Old", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(2026, 1, 1));
        Context.Add(preset);
        await Context.SaveChangesAsync();

        // Written the way the old code allowed: as typed, not normalized.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO BlockedSites (BlockPresetId, Url) VALUES ({preset.Id}, {"https://www.YouTube.com/watch"})");
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO BlockedApps (BlockPresetId, ProcessName) VALUES ({preset.Id}, {@"C:\Games\Steam.EXE"})");

        await using var fresh = CreateFreshContext();
        var loaded = await fresh.BlockPresets.SingleAsync(p => p.Id == preset.Id);

        loaded.Websites.Should().Equal(new Website("youtube.com"));
        loaded.BlockedApps.Should().Equal(new BlockedApp("steam"));
    }
}
