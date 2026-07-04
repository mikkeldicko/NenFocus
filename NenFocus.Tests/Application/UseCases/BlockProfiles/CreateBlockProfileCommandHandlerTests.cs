using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class CreateBlockProfileCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public CreateBlockProfileCommandHandlerTests() => _clock.UtcNow.Returns(Now);

    private CreateBlockProfileCommandHandler CreateHandler() => new(_profiles, _clock);

    private static CreateBlockProfileCommand ValidCommand() => new()
    {
        Name = "Deep Work",
        WebsitePolicy = WebsitePolicy.Blocklist,
        ProfileType = BlockType.Focus,
        BlockedApps = ["chrome"],
        Websites = ["facebook.com"]
    };

    [Fact]
    public async Task HandleAsync_NameAlreadyExists_Fails()
    {
        _profiles.ExistsWithNameAsync("Deep Work", cancellationToken: Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().HandleAsync(ValidCommand());

        result.Success.Should().BeFalse();
        await _profiles.DidNotReceive().AddAsync(Arg.Any<BlockProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CreatesProfileWithAppsAndSites()
    {
        var result = await CreateHandler().HandleAsync(ValidCommand());

        result.Success.Should().BeTrue();
        result.ProfileId.Should().NotBeNull();
        await _profiles.Received(1).AddAsync(
            Arg.Is<BlockProfile>(p =>
                p.Name == "Deep Work" &&
                p.BlockedApps.Any(a => a.ProcessName == "chrome") &&
                p.Websites.Any(s => s.Url == "facebook.com")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithTags_PersistsTags()
    {
        var command = ValidCommand() with { Tags = ["work", "focus"] };

        await CreateHandler().HandleAsync(command);

        await _profiles.Received(1).AddAsync(
            Arg.Is<BlockProfile>(p => p.Tags.SequenceEqual(new[] { "work", "focus" })),
            Arg.Any<CancellationToken>());
    }
}
