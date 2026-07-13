using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class DuplicateBlockPresetCommandHandlerTests
{
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private DuplicateBlockPresetCommandHandler CreateHandler() => new(_presets);

    private static BlockPreset CreateSourcePreset(Guid id)
        => BlockPreset.Create(id, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_SourceNotFound_Fails()
    {
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(
            new DuplicateBlockPresetCommand { SourcePresetId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithNewName_UsesProvidedName()
    {
        var sourceId = Guid.NewGuid();
        _presets.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourcePreset(sourceId));

        var result = await CreateHandler().HandleAsync(
            new DuplicateBlockPresetCommand { SourcePresetId = sourceId, NewName = "Weekend Mode" });

        result.Success.Should().BeTrue();
        await _presets.Received(1).AddAsync(
            Arg.Is<BlockPreset>(p => p.Name == "Weekend Mode"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithoutNewName_DefaultsToCopyOfSourceName()
    {
        var sourceId = Guid.NewGuid();
        _presets.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourcePreset(sourceId));

        await CreateHandler().HandleAsync(new DuplicateBlockPresetCommand { SourcePresetId = sourceId });

        await _presets.Received(1).AddAsync(
            Arg.Is<BlockPreset>(p => p.Name == "Copy of Deep Work"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NameCollision_AppendsTimestampSuffix()
    {
        var sourceId = Guid.NewGuid();
        _presets.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourcePreset(sourceId));
        _presets.ExistsWithNameAsync("Copy of Deep Work", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateHandler().HandleAsync(new DuplicateBlockPresetCommand { SourcePresetId = sourceId });

        await _presets.Received(1).AddAsync(
            Arg.Is<BlockPreset>(p => p.Name.StartsWith("Copy of Deep Work (") && p.Name.EndsWith(")")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExplicitBlankNewName_FailsCleanlyInsteadOfThrowing()
    {
        // An explicit "" (not null) skips the ?? default, so it reaches
        // BlockPreset.Duplicate/Create unchanged, which rejects a blank name.
        var sourceId = Guid.NewGuid();
        _presets.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourcePreset(sourceId));

        var result = await CreateHandler().HandleAsync(
            new DuplicateBlockPresetCommand { SourcePresetId = sourceId, NewName = "   " });

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().AddAsync(Arg.Any<BlockPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsNewPresetId()
    {
        var sourceId = Guid.NewGuid();
        _presets.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourcePreset(sourceId));

        var result = await CreateHandler().HandleAsync(new DuplicateBlockPresetCommand { SourcePresetId = sourceId });

        result.NewPresetId.Should().NotBeNull();
        result.NewPresetId.Should().NotBe(sourceId);
    }
}
