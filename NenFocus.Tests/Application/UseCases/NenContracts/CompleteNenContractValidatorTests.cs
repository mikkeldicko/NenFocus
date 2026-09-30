using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Application.UseCases.NenContracts.CompleteNenContract;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class CompleteNenContractValidatorTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10); // still days left — completing here ends it early
    private static readonly DateTime Now = new(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto> _readiness =
        Substitute.For<IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto>>();

    private CompleteNenContractValidator CreateValidator() => new(_contracts, _readiness);

    private static NenContract ActiveContract(double committedHours = 4) => NenContract.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private void StubReadiness(bool isReady, double remainingHours = 0, int pendingBlockCount = 0) =>
        _readiness.HandleAsync(Arg.Any<GetNenContractCompletionReadinessQuery>(), Arg.Any<CancellationToken>())
            .Returns(new NenContractCompletionReadinessDto
            {
                IsReady = isReady,
                RemainingHours = remainingHours,
                PendingBlockCount = pendingBlockCount
            });

    [Fact]
    public async Task Validate_ContractNotFound_IsInvalid()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotActive_IsInvalid()
    {
        var contract = ActiveContract();
        contract.Break(At.Utc(Now), At.UtcZone);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        StubReadiness(isReady: false); // Broken -> readiness query itself reports not ready

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ReadinessQuerySaysNotReady_RemainingHours_IsInvalidWithMessage()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubReadiness(isReady: false, remainingHours: 1);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("1h remaining"));
    }

    [Fact]
    public async Task Validate_ReadinessQuerySaysNotReady_PendingBlock_IsInvalidWithMessage()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubReadiness(isReady: false, pendingBlockCount: 1);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("still a block scheduled"));
    }

    [Fact]
    public async Task Validate_ReadinessQuerySaysReady_IsValid()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubReadiness(isReady: true);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeTrue();
    }
}
