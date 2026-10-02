using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Application.UseCases.NenContracts.CompleteNenContract;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class CompleteNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap =
        Substitute.For<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();

    public CompleteNenContractCommandHandlerTests()
    {
        _clock.SetNow(Now);
    }

    private CompleteNenContractCommandHandler CreateHandler() => new(_contracts, _clock, _recap);

    private static NenContract ActiveContract() => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_Fails()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ValidContract_CompletesAndSaves()
    {
        var contract = ActiveContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var recap = new NenContractRecapDto { ContractId = contract.Id };
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>()).Returns(recap);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = contract.Id });

        result.Success.Should().BeTrue();
        contract.Status.Should().Be(NenContractStatus.Completed);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidContract_ReturnsRecapForImmediatePostContractOverlay()
    {
        var contract = ActiveContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var recap = new NenContractRecapDto { ContractId = contract.Id, CompletionGrade = "Completed" };
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>()).Returns(recap);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = contract.Id });

        result.Recap.Should().Be(recap);
    }
}
