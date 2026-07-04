using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class BlockProfileTests
{
    private static BlockProfile CreateProfile(
        WebsitePolicy policy = WebsitePolicy.Blocklist,
        BlockType type = BlockType.Focus,
        IEnumerable<string>? tags = null)
        => BlockProfile.Create(Guid.NewGuid(), "Deep Work", policy, type, DateTime.UtcNow, tags);

    [Fact]
    public void Create_Valid_RaisesEventAndTrimsName()
    {
        var profile = BlockProfile.Create(
            Guid.NewGuid(), "  Deep Work  ", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);

        profile.Name.Should().Be("Deep Work");
        var raised = profile.DomainEvents.OfType<BlockProfileCreatedEvent>().Single();
        raised.Name.Should().Be("Deep Work");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string? blank)
    {
        var act = () => BlockProfile.Create(
            Guid.NewGuid(), blank!, WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithTags_TrimsEachTag()
    {
        var profile = CreateProfile(tags: ["  work  ", "focus"]);

        profile.Tags.Should().BeEquivalentTo(["work", "focus"]);
    }

    [Fact]
    public void ChangeProfileType_UpdatesType()
    {
        var profile = CreateProfile(type: BlockType.Focus);

        profile.ChangeProfileType(BlockType.Background);

        profile.ProfileType.Should().Be(BlockType.Background);
    }

    [Fact]
    public void Rename_Valid_TrimsName()
    {
        var profile = CreateProfile();

        profile.Rename("  New Name  ");

        profile.Name.Should().Be("New Name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Blank_Throws(string? blank)
    {
        var profile = CreateProfile();

        var act = () => profile.Rename(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddBlockedApp_Duplicate_IsIgnored()
    {
        var profile = CreateProfile();

        profile.AddBlockedApp(new BlockedApp("chrome"));
        profile.AddBlockedApp(new BlockedApp("chrome"));

        profile.BlockedApps.Should().ContainSingle();
    }

    [Fact]
    public void AddBlockedApp_DifferentCasing_IsTreatedAsDistinct()
    {
        var profile = CreateProfile();

        profile.AddBlockedApp(new BlockedApp("chrome"));
        profile.AddBlockedApp(new BlockedApp("Chrome"));

        profile.BlockedApps.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveBlockedApp_RemovesMatchingProcessName()
    {
        var profile = CreateProfile();
        profile.AddBlockedApp(new BlockedApp("chrome"));

        profile.RemoveBlockedApp(new BlockedApp("chrome"));

        profile.BlockedApps.Should().BeEmpty();
    }

    [Fact]
    public void AddWebsite_Duplicate_IsIgnoredRegardlessOfCasing()
    {
        var profile = CreateProfile();

        profile.AddWebsite(new BlockedSite("Facebook.com"));
        profile.AddWebsite(new BlockedSite("facebook.com"));

        profile.Websites.Should().ContainSingle();
    }

    [Fact]
    public void RemoveWebsite_RemovesMatchingUrl()
    {
        var profile = CreateProfile();
        profile.AddWebsite(new BlockedSite("facebook.com"));

        profile.RemoveWebsite(new BlockedSite("facebook.com"));

        profile.Websites.Should().BeEmpty();
    }

    [Fact]
    public void ChangeWebsitePolicy_ClearsExistingWebsites()
    {
        var profile = CreateProfile(WebsitePolicy.Blocklist);
        profile.AddWebsite(new BlockedSite("facebook.com"));

        profile.ChangeWebsitePolicy(WebsitePolicy.Allowlist);

        profile.WebsitePolicy.Should().Be(WebsitePolicy.Allowlist);
        profile.Websites.Should().BeEmpty();
    }

    [Fact]
    public void AddTag_Duplicate_IsIgnored()
    {
        var profile = CreateProfile();

        profile.AddTag("work");
        profile.AddTag("work");

        profile.Tags.Should().ContainSingle();
    }

    [Fact]
    public void AddTag_Blank_IsIgnored()
    {
        var profile = CreateProfile();

        profile.AddTag("   ");

        profile.Tags.Should().BeEmpty();
    }

    [Fact]
    public void RemoveTag_RemovesExactMatch()
    {
        var profile = CreateProfile();
        profile.AddTag("work");

        profile.RemoveTag("work");

        profile.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_CopiesPolicyTypeTagsAppsAndSites_UnderNewIdentity()
    {
        var original = CreateProfile(WebsitePolicy.Blocklist, BlockType.Focus, ["work"]);
        original.AddBlockedApp(new BlockedApp("chrome"));
        original.AddWebsite(new BlockedSite("facebook.com"));
        var newId = Guid.NewGuid();

        var copy = original.Duplicate("Deep Work Copy", newId, DateTime.UtcNow);

        copy.Id.Should().Be(newId);
        copy.Id.Should().NotBe(original.Id);
        copy.Name.Should().Be("Deep Work Copy");
        copy.WebsitePolicy.Should().Be(original.WebsitePolicy);
        copy.ProfileType.Should().Be(original.ProfileType);
        copy.Tags.Should().BeEquivalentTo(original.Tags);
        copy.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(
            original.BlockedApps.Select(a => a.ProcessName));
        copy.Websites.Select(s => s.Url).Should().BeEquivalentTo(
            original.Websites.Select(s => s.Url));
    }

    [Fact]
    public void Duplicate_MutatingCopyDoesNotAffectOriginal()
    {
        var original = CreateProfile();
        original.AddBlockedApp(new BlockedApp("chrome"));
        var copy = original.Duplicate("Copy", Guid.NewGuid(), DateTime.UtcNow);

        copy.AddBlockedApp(new BlockedApp("spotify"));

        original.BlockedApps.Should().ContainSingle();
        copy.BlockedApps.Should().HaveCount(2);
    }
}
