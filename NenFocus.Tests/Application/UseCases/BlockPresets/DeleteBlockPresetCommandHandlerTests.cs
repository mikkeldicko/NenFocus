using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class DeleteBlockPresetCommandHandlerTests
{
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private DeleteBlockPresetCommandHandler CreateHandler() => new(_presets);

    [Fact]
    public async Task HandleAsync_HappyPath_DeletesPreset()
    {
        var presetId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(new DeleteBlockPresetCommand { PresetId = presetId });

        result.Success.Should().BeTrue();
        await _presets.Received(1).DeleteAsync(presetId, Arg.Any<CancellationToken>());
    }
}
