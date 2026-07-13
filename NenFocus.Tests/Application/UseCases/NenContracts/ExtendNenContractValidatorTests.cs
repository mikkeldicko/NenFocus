using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Extend;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class ExtendNenContractValidatorTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private ExtendNenContractValidator CreateValidator() => new(_contracts, _presets);

    private static NenContract Seal(NenContractStatus targetStatus) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: StartDate,
        endDate: EndDate,
        strictnessRule: StrictnessRule.Locked,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: targetStatus == NenContractStatus.Active
            ? StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task Validate_EmptyContractId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(new ExtendNenContractCommand { ContractId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotFound_IsInvalid()
    {
        var id = Guid.NewGuid();
        _contracts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateValidator().ValidateAsync(
            new ExtendNenContractCommand { ContractId = id, NewEndDate = EndDate.AddDays(1) });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotActive_IsInvalid()
    {
        var contract = Seal(NenContractStatus.Scheduled);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = EndDate.AddDays(1) });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NewEndDateNotAfterCurrentEndDate_IsInvalid()
    {
        var contract = Seal(NenContractStatus.Active);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = EndDate });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var contract = Seal(NenContractStatus.Active);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = EndDate.AddDays(2) });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockEndNotAfterStart_IsInvalid()
    {
        var contract = Seal(NenContractStatus.Active);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 8, 10, 0, 0), new DateTime(2026, 1, 8, 9, 0, 0), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = EndDate.AddDays(2), Blocks = [block] });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockPresetNotFound_IsInvalid()
    {
        var contract = Seal(NenContractStatus.Active);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);
        var block = new NenContractBlockSpec(presetId,
            new DateTime(2026, 1, 8, 9, 0, 0), new DateTime(2026, 1, 8, 10, 0, 0), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = EndDate.AddDays(2), Blocks = [block] });

        result.IsValid.Should().BeFalse();
    }
}
