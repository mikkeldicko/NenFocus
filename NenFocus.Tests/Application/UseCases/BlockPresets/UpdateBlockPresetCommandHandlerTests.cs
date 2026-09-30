using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class UpdateBlockPresetCommandHandlerTests
{
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private UpdateBlockPresetCommandHandler CreateHandler() => new(_presets);

    private static BlockPreset CreatePreset(Guid id)
    {
        var preset = BlockPreset.Create(id, "Old Name", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow));
        preset.AddBlockedApp(new BlockedApp("spotify"));
        preset.AddWebsite(new BlockedSite("reddit.com"));
        preset.AddTag("old-tag");
        return preset;
    }

    private static UpdateBlockPresetCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        Name = "New Name",
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        PresetType = BlockPresetType.Focus,
        BlockedApps = ["chrome"],
        Websites = ["facebook.com"],
        Tags = ["new-tag"]
    };

    [Fact]
    public async Task HandleAsync_PresetNotFound_Fails()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(presetId));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NameAlreadyExists_Fails()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(CreatePreset(presetId));
        _presets.ExistsWithNameAsync("New Name", BlockPresetType.Focus, excludeId: presetId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateHandler().HandleAsync(ValidCommand(presetId));

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().UpdateAsync(Arg.Any<BlockPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReplacesAppsSitesAndTagsEntirely()
    {
        var presetId = Guid.NewGuid();
        var preset = CreatePreset(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(preset);

        var result = await CreateHandler().HandleAsync(ValidCommand(presetId));

        result.Success.Should().BeTrue();
        preset.Name.Should().Be("New Name");
        preset.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(["chrome"]);
        preset.Websites.Select(s => s.Url).Should().BeEquivalentTo(["facebook.com"]);
        preset.Tags.Should().BeEquivalentTo(["new-tag"]);
        await _presets.Received(1).UpdateAsync(preset, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankBlockedAppNameBypassingValidator_FailsWithoutMutatingPreset()
    {
        // Proves the fix for the partial-mutation risk: TransactionalCommandHandler
        // commits unconditionally, so if this handler mutated preset before discovering
        // the bad app name, the partial mutation would still get persisted despite the
        // Fail result. Asserting the preset is completely untouched is the real test.
        var presetId = Guid.NewGuid();
        var preset = CreatePreset(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(preset);
        var command = ValidCommand(presetId) with { BlockedApps = ["   "] };

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeFalse();
        preset.Name.Should().Be("Old Name");
        preset.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(["spotify"]);
        await _presets.DidNotReceive().UpdateAsync(Arg.Any<BlockPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankName_FailsWithoutMutatingPreset()
    {
        var presetId = Guid.NewGuid();
        var preset = CreatePreset(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(preset);
        var command = ValidCommand(presetId) with { Name = "   " };

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeFalse();
        preset.Name.Should().Be("Old Name");
        preset.BlockedApps.Select(a => a.ProcessName).Should().BeEquivalentTo(["spotify"]);
    }

    [Fact]
    public async Task HandleAsync_PresetTypeChanged_UpdatesPresetType()
    {
        var presetId = Guid.NewGuid();
        var preset = CreatePreset(presetId); // Focus
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(preset);

        var command = ValidCommand(presetId) with { PresetType = BlockPresetType.Background };
        await CreateHandler().HandleAsync(command);

        preset.PresetType.Should().Be(BlockPresetType.Background);
    }
}
