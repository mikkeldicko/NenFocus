using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class UpdateBlockProfileCommandHandlerTests
{
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private UpdateBlockProfileCommandHandler CreateHandler() => new(_profiles);

    private static BlockProfile CreateProfile(Guid id)
    {
        var profile = BlockProfile.Create(id, "Old Name", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);
        profile.AddBlockedApp(new BlockedApp("spotify"));
        profile.AddWebsite(new BlockedSite("reddit.com"));
        profile.AddTag("old-tag");
        return profile;
    }

    private static UpdateBlockProfileCommand ValidCommand(Guid profileId) => new()
    {
        ProfileId = profileId,
        Name = "New Name",
        WebsitePolicy = WebsitePolicy.Blocklist,
        ProfileType = BlockType.Focus,
        BlockedApps = ["chrome"],
        Websites = ["facebook.com"],
        Tags = ["new-tag"]
    };

    [Fact]
    public async Task HandleAsync_ProfileNotFound_Fails()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(profileId));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NameAlreadyExists_Fails()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(CreateProfile(profileId));
        _profiles.ExistsWithNameAsync("New Name", excludeId: profileId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateHandler().HandleAsync(ValidCommand(profileId));

        result.Success.Should().BeFalse();
        await _profiles.DidNotReceive().UpdateAsync(Arg.Any<BlockProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReplacesAppsSitesAndTagsEntirely()
    {
        var profileId = Guid.NewGuid();
        var profile = CreateProfile(profileId);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(profile);

        var result = await CreateHandler().HandleAsync(ValidCommand(profileId));

        result.Success.Should().BeTrue();
        profile.Name.Should().Be("New Name");
        profile.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(["chrome"]);
        profile.Websites.Select(s => s.Url).Should().BeEquivalentTo(["facebook.com"]);
        profile.Tags.Should().BeEquivalentTo(["new-tag"]);
        await _profiles.Received(1).UpdateAsync(profile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ProfileTypeChanged_UpdatesProfileType()
    {
        var profileId = Guid.NewGuid();
        var profile = CreateProfile(profileId); // Focus
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(profile);

        var command = ValidCommand(profileId) with { ProfileType = BlockType.Background };
        await CreateHandler().HandleAsync(command);

        profile.ProfileType.Should().Be(BlockType.Background);
    }
}
