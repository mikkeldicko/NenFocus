using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class CreateBlockPresetCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public CreateBlockPresetCommandHandlerTests() => _clock.UtcNow.Returns(Now);

    private CreateBlockPresetCommandHandler CreateHandler() => new(_presets, _clock);

    private static CreateBlockPresetCommand ValidCommand() => new()
    {
        Name = "Deep Work",
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        PresetType = BlockPresetType.Focus,
        BlockedApps = ["chrome"],
        Websites = ["facebook.com"]
    };

    [Fact]
    public async Task HandleAsync_NameAlreadyExists_Fails()
    {
        _presets.ExistsWithNameAsync("Deep Work", BlockPresetType.Focus, cancellationToken: Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().HandleAsync(ValidCommand());

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().AddAsync(Arg.Any<BlockPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CreatesPresetWithAppsAndSites()
    {
        var result = await CreateHandler().HandleAsync(ValidCommand());

        result.Success.Should().BeTrue();
        result.PresetId.Should().NotBeNull();
        await _presets.Received(1).AddAsync(
            Arg.Is<BlockPreset>(p =>
                p.Name == "Deep Work" &&
                p.BlockedApps.Any(a => a.ProcessName == "chrome") &&
                p.Websites.Any(s => s.Url == "facebook.com")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankBlockedAppNameBypassingValidator_FailsCleanlyInsteadOfThrowing()
    {
        // The validator doesn't check individual app/site strings for blankness, only
        // that the collections aren't empty — BlockedApp's own constructor is the only
        // thing that would reject "" or whitespace. Proves the handler doesn't crash.
        var command = ValidCommand() with { BlockedApps = ["   "] };

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().AddAsync(Arg.Any<BlockPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithTags_PersistsTags()
    {
        var command = ValidCommand() with { Tags = ["work", "focus"] };

        await CreateHandler().HandleAsync(command);

        await _presets.Received(1).AddAsync(
            Arg.Is<BlockPreset>(p => p.Tags.SequenceEqual(new[] { "work", "focus" })),
            Arg.Any<CancellationToken>());
    }
}
