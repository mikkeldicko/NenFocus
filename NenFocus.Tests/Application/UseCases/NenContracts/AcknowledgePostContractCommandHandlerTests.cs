using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.AcknowledgePostContract;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class AcknowledgePostContractCommandHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime Now = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public AcknowledgePostContractCommandHandlerTests() => _clock.SetNow(Now);

    private AcknowledgePostContractCommandHandler CreateHandler() => new(_contracts, _clock);

    private static NenContract CompletedContract() => TestContracts.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_Fails()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new AcknowledgePostContractCommand { ContractId = ContractId }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CompletedContract_StampsAndSaves()
    {
        var contract = CompletedContract();
        contract.Complete(At.Utc(Now));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new AcknowledgePostContractCommand { ContractId = ContractId }, CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.PostContractAcknowledgedAt.Should().Be(At.Utc(Now));
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StillActiveContract_Fails()
    {
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new AcknowledgePostContractCommand { ContractId = ContractId }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }
}
