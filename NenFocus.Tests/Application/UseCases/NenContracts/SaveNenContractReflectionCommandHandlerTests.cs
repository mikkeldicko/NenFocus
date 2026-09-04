using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SaveReflection;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class SaveNenContractReflectionCommandHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private SaveNenContractReflectionCommandHandler CreateHandler() => new(_contracts);

    private static NenContract CompletedContract()
    {
        var contract = NenContract.Seal(
            id: ContractId, name: "Focused Week", goal: new NenContractGoal("Ship it"),
            startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.Locked,
            dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        contract.Complete(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return contract;
    }

    [Fact]
    public async Task HandleAsync_ContractNotFound_Fails()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new SaveNenContractReflectionCommand { ContractId = ContractId, Reflection = "Went well" }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CompletedContract_SavesReflection()
    {
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(
            new SaveNenContractReflectionCommand { ContractId = ContractId, Reflection = "  Went well  " }, CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.Reflection.Should().Be("Went well");
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }
}
