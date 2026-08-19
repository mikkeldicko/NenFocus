using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.ContractPresets;

public class UpdateContractPresetTests
{
    private readonly IContractPresetRepository _presets = Substitute.For<IContractPresetRepository>();

    public UpdateContractPresetTests()
    {
        _presets.ExistsWithNameAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private UpdateContractPresetCommandHandler CreateHandler() => new(_presets);
    private UpdateContractPresetValidator CreateValidator() => new();

    private static UpdateContractPresetCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        Name = "Renamed",
        StrictnessRule = StrictnessRule.DailyFlexibility,
        DailyFloorHours = 5,
        AllowCancellation = true
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyPresetId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(25)]
    public async Task Validate_DailyFloorOutOfRange_IsInvalid(double hours)
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()) with { DailyFloorHours = hours });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFloorNotWholeNumber_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()) with { DailyFloorHours = 3.5 });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFloorNull_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()) with { DailyFloorHours = null });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DailyFloorExceedsWorkHoursWindow_IsInvalid()
    {
        var command = ValidCommand(Guid.NewGuid()) with
        {
            DailyFloorHours = 6,
            WorkHoursStart = new TimeOnly(10, 0),
            WorkHoursEnd = new TimeOnly(15, 0)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_InvalidDailyFloorBypassingValidator_FailsCleanlyInsteadOfThrowing()
    {
        var presetId = Guid.NewGuid();
        var existing = ContractPreset.Create(presetId, "Original", StrictnessRule.Locked, new DailyFloor(4), null, false);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(existing);
        var command = ValidCommand(presetId) with { DailyFloorHours = 99 };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().UpdateAsync(Arg.Any<ContractPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_Fails()
    {
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContractPreset?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(Guid.NewGuid()), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NameAlreadyExistsOnAnotherPreset_Fails()
    {
        var presetId = Guid.NewGuid();
        var existing = ContractPreset.Create(presetId, "Original", StrictnessRule.Locked, new DailyFloor(4), null, false);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(existing);
        _presets.ExistsWithNameAsync("Renamed", excludeId: presetId, cancellationToken: Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().HandleAsync(ValidCommand(presetId), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().UpdateAsync(Arg.Any<ContractPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesEveryField()
    {
        var presetId = Guid.NewGuid();
        var existing = ContractPreset.Create(presetId, "Original", StrictnessRule.Locked, new DailyFloor(4), null, false);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(existing);
        var command = ValidCommand(presetId) with
        {
            WorkHoursStart = new TimeOnly(9, 0),
            WorkHoursEnd = new TimeOnly(17, 0)
        };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        existing.Name.Should().Be("Renamed");
        existing.StrictnessRule.Should().Be(StrictnessRule.DailyFlexibility);
        existing.DailyFloor!.Hours.Should().Be(5);
        existing.WorkHours!.Start.Should().Be(new TimeOnly(9, 0));
        existing.AllowCancellation.Should().BeTrue();
        await _presets.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExcludesSelfFromUniquenessCheck()
    {
        var presetId = Guid.NewGuid();
        var existing = ContractPreset.Create(presetId, "Original", StrictnessRule.Locked, new DailyFloor(4), null, false);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateHandler().HandleAsync(ValidCommand(presetId), CancellationToken.None);

        await _presets.Received(1).ExistsWithNameAsync("Renamed", excludeId: presetId, cancellationToken: Arg.Any<CancellationToken>());
    }
}
