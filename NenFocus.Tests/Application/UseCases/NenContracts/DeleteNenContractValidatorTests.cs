using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class DeleteNenContractValidatorTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private DeleteNenContractValidator CreateValidator() => new(_contracts);

    private static NenContract Seal(DateTime sealedAt) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Summer Focus",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(sealedAt), timeZone: At.UtcZone);

    [Fact]
    public async Task Validate_EmptyContractId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(new DeleteNenContractCommand { ContractId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotFound_IsInvalid()
    {
        var id = Guid.NewGuid();
        _contracts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateValidator().ValidateAsync(new DeleteNenContractCommand { ContractId = id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ScheduledContract_IsValid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Scheduled
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new DeleteNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ActiveContract_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new DeleteNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CompletedContract_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        contract.Complete(At.Utc(StartDate.AddDays(10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new DeleteNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeFalse();
    }
}
