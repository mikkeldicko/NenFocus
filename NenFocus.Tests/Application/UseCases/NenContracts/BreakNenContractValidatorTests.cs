using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class BreakNenContractValidatorTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private BreakNenContractValidator CreateValidator() => new(_contracts);

    private static NenContract Seal(bool allowCancellation, DateTime sealedAt) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: StartDate,
        endDate: EndDate,
        strictnessRule: StrictnessRule.DailyFlexibility,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: allowCancellation,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: sealedAt);

    [Fact]
    public async Task Validate_EmptyContractId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotFound_IsInvalid()
    {
        var id = Guid.NewGuid();
        _contracts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ScheduledAndCancellable_IsValid()
    {
        // Not yet started, but still in effect — cancellable before Day 1.
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Scheduled
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractAlreadyCompleted_IsInvalid()
    {
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        contract.Complete(StartDate.AddDays(10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CancellationNotAllowed_StillValid_InterimUntilStrictModeExists()
    {
        // AllowCancellation is deliberately not enforced right now — see BreakNenContractValidator's own comment.
        var contract = Seal(allowCancellation: false, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ActiveAndCancellable_IsValid()
    {
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeTrue();
    }
}
