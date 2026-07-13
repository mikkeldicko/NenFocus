using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.ContractPresets;

public class DeleteContractPresetTests
{
    private readonly IContractPresetRepository _presets = Substitute.For<IContractPresetRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    public DeleteContractPresetTests()
    {
        _contracts.HasContractsInEffectForPresetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private DeleteContractPresetCommandHandler CreateHandler() => new(_presets);
    private DeleteContractPresetValidator CreateValidator() => new(_contracts);

    [Fact]
    public async Task Validate_NoContractsInEffect_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(new DeleteContractPresetCommand { PresetId = Guid.NewGuid() });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractInEffectForPreset_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _contracts.HasContractsInEffectForPresetAsync(presetId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateValidator().ValidateAsync(new DeleteContractPresetCommand { PresetId = presetId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_DeletesPreset()
    {
        var presetId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(new DeleteContractPresetCommand { PresetId = presetId }, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _presets.Received(1).DeleteAsync(presetId, Arg.Any<CancellationToken>());
    }
}
