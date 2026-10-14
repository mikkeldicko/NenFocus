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
        BlockPresetType type = BlockPresetType.Focus)
        => BlockPreset.Create(Guid.NewGuid(), "Deep Work", policy, type, At.Utc(DateTime.UtcNow));

    [Fact]
    public void Create_Valid_RaisesEventAndTrimsName()
    {
        var preset = BlockPreset.Create(
            Guid.NewGuid(), "  Deep Work  ", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow));

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
            Guid.NewGuid(), blank!, WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow));

        act.Should().Throw<ArgumentException>();
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
    public void AddBlockedApp_DifferentCasingOrAPath_IsTheSameApp()
    {
        // Gap 112 (I5): BlockedApp is the one definition of an app — Windows matches process
        // names case-insensitively, so "Chrome" and a picked path to chrome.exe are one app.
        var preset = CreatePreset();

        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddBlockedApp(new BlockedApp("Chrome"));
        preset.AddBlockedApp(new BlockedApp(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));

        preset.BlockedApps.Should().ContainSingle();
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

        preset.AddWebsite(new Website("Facebook.com"));
        preset.AddWebsite(new Website("facebook.com"));

        preset.Websites.Should().ContainSingle();
    }

    [Fact]
    public void RemoveWebsite_RemovesMatchingUrl()
    {
        var preset = CreatePreset();
        preset.AddWebsite(new Website("facebook.com"));

        preset.RemoveWebsite(new Website("facebook.com"));

        preset.Websites.Should().BeEmpty();
    }

    [Fact]
    public void ChangeWebsiteFilterType_ClearsExistingWebsites()
    {
        var preset = CreatePreset(WebsiteFilterType.Blocklist);
        preset.AddWebsite(new Website("facebook.com"));

        preset.ChangeWebsiteFilterType(WebsiteFilterType.Allowlist);

        preset.WebsiteFilterType.Should().Be(WebsiteFilterType.Allowlist);
        preset.Websites.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_CopiesPolicyTypeAppsAndSites_UnderNewIdentity()
    {
        var original = CreatePreset(WebsiteFilterType.Blocklist, BlockPresetType.Focus);
        original.AddBlockedApp(new BlockedApp("chrome"));
        original.AddWebsite(new Website("facebook.com"));
        var newId = Guid.NewGuid();

        var copy = original.Duplicate("Deep Work Copy", newId, At.Utc(DateTime.UtcNow));

        copy.Id.Should().Be(newId);
        copy.Id.Should().NotBe(original.Id);
        copy.Name.Should().Be("Deep Work Copy");
        copy.WebsiteFilterType.Should().Be(original.WebsiteFilterType);
        copy.PresetType.Should().Be(original.PresetType);
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
        var copy = original.Duplicate("Copy", Guid.NewGuid(), At.Utc(DateTime.UtcNow));

        copy.AddBlockedApp(new BlockedApp("spotify"));

        original.BlockedApps.Should().ContainSingle();
        copy.BlockedApps.Should().HaveCount(2);
    }

    private static HashSet<BlockedApp> Apps(params string[] names) => names.Select(n => new BlockedApp(n)).ToHashSet();
    private static HashSet<Website> Sites(params string[] urls) => urls.Select(u => new Website(u)).ToHashSet();

    [Fact]
    public void IsAtLeastAsStrict_BlocklistAddingApp_True()
    {
        var preset = CreatePreset();
        preset.AddBlockedApp(new BlockedApp("chrome"));

        preset.IsAtLeastAsStrict(Apps("chrome", "firefox"), Sites()).Should().BeTrue();
    }

    [Fact]
    public void IsAtLeastAsStrict_RemovingApp_False()
    {
        var preset = CreatePreset();
        preset.AddBlockedApp(new BlockedApp("chrome"));
        preset.AddBlockedApp(new BlockedApp("firefox"));

        preset.IsAtLeastAsStrict(Apps("chrome"), Sites()).Should().BeFalse();
    }

    [Fact]
    public void IsAtLeastAsStrict_BlocklistAddingSite_True()
    {
        var preset = CreatePreset(policy: WebsiteFilterType.Blocklist);
        preset.AddWebsite(new Website("reddit.com"));

        preset.IsAtLeastAsStrict(Apps(), Sites("reddit.com", "youtube.com")).Should().BeTrue();
    }

    [Fact]
    public void IsAtLeastAsStrict_BlocklistRemovingSite_False()
    {
        var preset = CreatePreset(policy: WebsiteFilterType.Blocklist);
        preset.AddWebsite(new Website("reddit.com"));
        preset.AddWebsite(new Website("youtube.com"));

        preset.IsAtLeastAsStrict(Apps(), Sites("reddit.com")).Should().BeFalse();
    }

    [Fact]
    public void IsAtLeastAsStrict_AllowlistRemovingSite_True()
    {
        // Removing an allowed site narrows what's let through — the "stricter" direction,
        // the inverse of Blocklist.
        var preset = CreatePreset(policy: WebsiteFilterType.Allowlist);
        preset.AddWebsite(new Website("allowed-a.com"));
        preset.AddWebsite(new Website("allowed-b.com"));

        preset.IsAtLeastAsStrict(Apps(), Sites("allowed-a.com")).Should().BeTrue();
    }

    [Fact]
    public void IsAtLeastAsStrict_AllowlistAddingSite_False()
    {
        // Adding an allowed site widens what's let through — the "weaker" direction, even
        // though it's technically an addition.
        var preset = CreatePreset(policy: WebsiteFilterType.Allowlist);
        preset.AddWebsite(new Website("allowed-a.com"));

        preset.IsAtLeastAsStrict(Apps(), Sites("allowed-a.com", "allowed-b.com")).Should().BeFalse();
    }
}
