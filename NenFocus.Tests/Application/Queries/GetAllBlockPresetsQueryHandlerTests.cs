using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetAllBlockPresetsQueryHandlerTests
{
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private GetAllBlockPresetsQueryHandler CreateHandler() => new(_presets);

    private static BlockPreset CreatePreset(string name, BlockPresetType type)
        => BlockPreset.Create(Guid.NewGuid(), name, WebsiteFilterType.Blocklist, type, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_NoTagFilter_ReturnsAllPresets()
    {
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockPreset>)[CreatePreset("A", BlockPresetType.Focus), CreatePreset("B", BlockPresetType.Background)]);

        var result = await CreateHandler().HandleAsync(new GetAllBlockPresetsQuery());

        result.Select(p => p.Name).Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public async Task HandleAsync_WithTag_QueriesByTagInsteadOfAll()
    {
        _presets.GetByTagsAsync(Arg.Is<IEnumerable<string>>(t => t.Contains("work")), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<BlockPreset>)[CreatePreset("Work Preset", BlockPresetType.Focus)]);

        var result = await CreateHandler().HandleAsync(new GetAllBlockPresetsQuery { Tag = "work" });

        result.Should().ContainSingle(p => p.Name == "Work Preset");
        await _presets.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithPresetTypeFilter_ReturnsOnlyMatchingType()
    {
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockPreset>)[CreatePreset("Focus One", BlockPresetType.Focus), CreatePreset("Bg One", BlockPresetType.Background)]);

        var result = await CreateHandler().HandleAsync(new GetAllBlockPresetsQuery { PresetType = BlockPresetType.Background });

        result.Should().ContainSingle(p => p.Name == "Bg One");
    }
}
