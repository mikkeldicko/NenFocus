using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class BlockPresetTests
{
    private static BlockPreset CreatePreset(
        WebsiteFilterType policy = WebsiteFilterType.Blocklist,
        BlockPresetType type = BlockPresetType.Focus,
        IEnumerable<string>? tags = null)
        => BlockPreset.Create(Guid.NewGuid(), "Deep Work", policy, type, DateTime.UtcNow, tags);

    [Fact]
    public void Create_Valid_RaisesEventAndTrimsName()
    {
        var preset = BlockPreset.Create(
            Guid.NewGuid(), "  Deep Work  ", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);

        preset.Name.Should().Be("Deep Work");
        var raised = preset.DomainEvents.OfType<BlockPresetCreatedEvent>().Single();
        raised.Name.Should().Be("Deep Work");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string? blank)
    {
        var act = () => BlockPreset.Create(
            Guid.NewGuid(), blank!, WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithTags_TrimsEachTag()
    {
        var preset = CreatePreset(tags: ["  work  ", "focus"]);

        preset.Tags.Should().BeEquivalentTo(["work", "focus"]);
    }

    [Fact]
    public void ChangePresetType_UpdatesType()
    {
        var preset = CreatePreset(type: BlockPresetType.Focus);

        preset.ChangePresetType(BlockPresetType.Background);

        preset.PresetType.Should().Be(BlockPresetType.Background);
    }

    [Fact]
    public void Rename_Valid_TrimsName()
    {
        var preset = CreatePreset();

        preset.Rename("  New Name  ");

        preset.Name.Should().Be("New Name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Blank_Throws(string? blank)
    {
        var preset = CreatePreset();

        var act = () => preset.Rename(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddBlockedApp_Duplicate_IsIgnored()
    {
        var preset = CreatePreset();

        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddBlockedApp(new BlockedApp("chrome"));

        preset.BlockedApps.Should().ContainSingle();
    }

    [Fact]
    public void AddBlockedApp_DifferentCasing_IsTreatedAsDistinct()
    {
        var preset = CreatePreset();

        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddBlockedApp(new BlockedApp("Chrome"));

        preset.BlockedApps.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveBlockedApp_RemovesMatchingProcessName()
    {
        var preset = CreatePreset();
        preset.AddBlockedApp(new BlockedApp("chrome"));

        preset.RemoveBlockedApp(new BlockedApp("chrome"));

        preset.BlockedApps.Should().BeEmpty();
    }

    [Fact]
    public void AddWebsite_Duplicate_IsIgnoredRegardlessOfCasing()
    {
        var preset = CreatePreset();

        preset.AddWebsite(new BlockedSite("Facebook.com"));
        preset.AddWebsite(new BlockedSite("facebook.com"));

        preset.Websites.Should().ContainSingle();
    }

    [Fact]
    public void RemoveWebsite_RemovesMatchingUrl()
    {
        var preset = CreatePreset();
        preset.AddWebsite(new BlockedSite("facebook.com"));

        preset.RemoveWebsite(new BlockedSite("facebook.com"));

        preset.Websites.Should().BeEmpty();
    }

    [Fact]
    public void ChangeWebsiteFilterType_ClearsExistingWebsites()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        preset.AddWebsite(new BlockedSite("facebook.com"));

        preset.ChangeWebsiteFilterType(WebsiteFilterType.Allowlist);

        preset.WebsiteFilterType.Should().Be(WebsiteFilterType.Allowlist);
        preset.Websites.Should().BeEmpty();
    }

    [Fact]
    public void AddTag_Duplicate_IsIgnored()
    {
        var preset = CreatePreset();

        preset.AddTag("work");
        preset.AddTag("work");

        preset.Tags.Should().ContainSingle();
    }

    [Fact]
    public void AddTag_Blank_IsIgnored()
    {
        var preset = CreatePreset();

        preset.AddTag("   ");

        preset.Tags.Should().BeEmpty();
    }

    [Fact]
    public void RemoveTag_RemovesExactMatch()
    {
        var preset = CreatePreset();
        preset.AddTag("work");

        preset.RemoveTag("work");

        preset.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_CopiesPolicyTypeTagsAppsAndSites_UnderNewIdentity()
    {
        var original = CreatePreset(WebsiteFilterType.Blocklist, BlockPresetType.Focus, ["work"]);
        original.AddBlockedApp(new BlockedApp("chrome"));
        original.AddWebsite(new BlockedSite("facebook.com"));
        var newId = Guid.NewGuid();

        var copy = original.Duplicate("Deep Work Copy", newId, DateTime.UtcNow);

        copy.Id.Should().Be(newId);
        copy.Id.Should().NotBe(original.Id);
        copy.Name.Should().Be("Deep Work Copy");
        copy.WebsiteFilterType.Should().Be(original.WebsiteFilterType);
        copy.PresetType.Should().Be(original.PresetType);
        copy.Tags.Should().BeEquivalentTo(original.Tags);
        copy.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(
            original.BlockedApps.Select(a => a.ProcessName));
        copy.Websites.Select(s => s.Url).Should().BeEquivalentTo(
            original.Websites.Select(s => s.Url));
    }

    [Fact]
    public void Duplicate_MutatingCopyDoesNotAffectOriginal()
    {
        var original = CreatePreset();
        original.AddBlockedApp(new BlockedApp("chrome"));
        var copy = original.Duplicate("Copy", Guid.NewGuid(), DateTime.UtcNow);

        copy.AddBlockedApp(new BlockedApp("spotify"));

        original.BlockedApps.Should().ContainSingle();
        copy.BlockedApps.Should().HaveCount(2);
    }
}
