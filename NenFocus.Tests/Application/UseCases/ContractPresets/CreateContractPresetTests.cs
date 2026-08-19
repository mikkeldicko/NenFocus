using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.ContractPresets;

public class CreateContractPresetTests
{
    private readonly IContractPresetRepository _presets = Substitute.For<IContractPresetRepository>();

    public CreateContractPresetTests()
    {
        _presets.ExistsWithNameAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private CreateContractPresetCommandHandler CreateHandler() => new(_presets);
    private CreateContractPresetValidator CreateValidator() => new();

    private static CreateContractPresetCommand ValidCommand() => new()
    {
        Name = "Study Month",
        StrictnessRule = StrictnessRule.Locked,
        DailyFloorHours = 4,
        AllowCancellation = false
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyName_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Name = "" });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(25)]
    public async Task Validate_DailyFloorOutOfRange_IsInvalid(double hours)
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { DailyFloorHours = hours });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFloorNotWholeNumber_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { DailyFloorHours = 3.5 });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFloorNull_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { DailyFloorHours = null });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DailyFloorExceedsWorkHoursWindow_IsInvalid()
    {
        var command = ValidCommand() with
        {
            DailyFloorHours = 6,
            WorkHoursStart = new TimeOnly(10, 0),
            WorkHoursEnd = new TimeOnly(15, 0) // 5h window — a 6h floor could never fit
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFloorExactlyFillsWorkHoursWindow_IsValid()
    {
        var command = ValidCommand() with
        {
            DailyFloorHours = 5,
            WorkHoursStart = new TimeOnly(10, 0),
            WorkHoursEnd = new TimeOnly(15, 0)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OnlyWorkHoursStartSet_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { WorkHoursStart = new TimeOnly(9, 0) });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_WorkHoursEndBeforeStart_IsInvalid()
    {
        var command = ValidCommand() with { WorkHoursStart = new TimeOnly(17, 0), WorkHoursEnd = new TimeOnly(9, 0) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BothWorkHoursSetCorrectly_IsValid()
    {
        var command = ValidCommand() with { WorkHoursStart = new TimeOnly(9, 0), WorkHoursEnd = new TimeOnly(17, 0) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NameAlreadyExists_Fails()
    {
        _presets.ExistsWithNameAsync("Study Month", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().AddAsync(Arg.Any<ContractPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_Succeeds()
    {
        var result = await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PresetId.Should().NotBeNull();
        await _presets.Received(1).AddAsync(Arg.Any<ContractPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_MapsAllFieldsOntoEntity()
    {
        ContractPreset? saved = null;
        await _presets.AddAsync(Arg.Do<ContractPreset>(p => saved = p), Arg.Any<CancellationToken>());
        var command = ValidCommand() with
        {
            StrictnessRule = StrictnessRule.PeriodTimeBudget,
            DailyFloorHours = 6,
            WorkHoursStart = new TimeOnly(9, 0),
            WorkHoursEnd = new TimeOnly(17, 0),
            AllowCancellation = true
        };

        await CreateHandler().HandleAsync(command, CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Study Month");
        saved.StrictnessRule.Should().Be(StrictnessRule.PeriodTimeBudget);
        saved.DailyFloor!.Hours.Should().Be(6);
        saved.WorkHours!.Start.Should().Be(new TimeOnly(9, 0));
        saved.WorkHours.End.Should().Be(new TimeOnly(17, 0));
        saved.AllowCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_InvalidDailyFloorBypassingValidator_FailsCleanlyInsteadOfThrowing()
    {
        // The validator normally rejects this before the handler ever sees it — this
        // proves the handler defends its own boundary too, matching
        // SealNenContractCommandHandler's precedent, not just trusting the validator.
        var command = ValidCommand() with { DailyFloorHours = -1 };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().AddAsync(Arg.Any<ContractPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_InvalidWorkHoursBypassingValidator_FailsCleanlyInsteadOfThrowing()
    {
        var command = ValidCommand() with { WorkHoursStart = new TimeOnly(17, 0), WorkHoursEnd = new TimeOnly(9, 0) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _presets.DidNotReceive().AddAsync(Arg.Any<ContractPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoWorkHoursGiven_EntityWorkHoursIsNull()
    {
        ContractPreset? saved = null;
        await _presets.AddAsync(Arg.Do<ContractPreset>(p => saved = p), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(ValidCommand(), CancellationToken.None);

        saved!.WorkHours.Should().BeNull();
    }
}
