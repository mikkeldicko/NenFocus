using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class SweepNenContractLifecycleCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SweepNenContractLifecycleCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _contracts.GetDueForActivationAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[]);
        _contracts.GetDueForCompletionAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[]);
    }

    private SweepNenContractLifecycleCommandHandler CreateHandler() => new(_contracts, _clock);

    private static NenContract ScheduledContract(DateOnly startDate) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: startDate,
        endDate: startDate.AddDays(6),
        strictnessRule: StrictnessRule.Locked,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: startDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // seals as Scheduled

    private static NenContract ActiveContract(DateOnly startDate, DateOnly endDate)
    {
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            startDate: startDate,
            endDate: endDate,
            strictnessRule: StrictnessRule.Locked,
            dailyFloorPolicy: DailyFloorPolicy.None,
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // seals as Active
        return contract;
    }

    [Fact]
    public async Task HandleAsync_NothingDue_ReturnsEmptyResultAndSavesNothing()
    {
        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        result.ActivatedContractIds.Should().BeEmpty();
        result.CompletedContractIds.Should().BeEmpty();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractDueForActivation_ActivatesSavesAndReportsIt()
    {
        var contract = ScheduledContract(DateOnly.FromDateTime(Now));
        contract.Status.Should().Be(NenContractStatus.Scheduled);
        _contracts.GetDueForActivationAsync(Now, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Active);
        result.ActivatedContractIds.Should().ContainSingle().Which.Should().Be(contract.Id);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractDueForCompletion_CompletesSavesAndReportsIt()
    {
        var contract = ActiveContract(DateOnly.FromDateTime(Now).AddDays(-10), DateOnly.FromDateTime(Now).AddDays(-1));
        contract.Status.Should().Be(NenContractStatus.Active);
        _contracts.GetDueForCompletionAsync(Now, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Completed);
        result.CompletedContractIds.Should().ContainSingle().Which.Should().Be(contract.Id);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BothActivationsAndCompletionsDue_HandlesBothInOneSweep()
    {
        var toActivate = ScheduledContract(DateOnly.FromDateTime(Now));
        var toComplete = ActiveContract(DateOnly.FromDateTime(Now).AddDays(-10), DateOnly.FromDateTime(Now).AddDays(-1));

        _contracts.GetDueForActivationAsync(Now, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[toActivate]);
        _contracts.GetDueForCompletionAsync(Now, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[toComplete]);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        result.ActivatedContractIds.Should().ContainSingle().Which.Should().Be(toActivate.Id);
        result.CompletedContractIds.Should().ContainSingle().Which.Should().Be(toComplete.Id);
    }

    [Fact]
    public async Task HandleAsync_QueriesBothRepositoryMethodsUsingClockNow()
    {
        await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        await _contracts.Received(1).GetDueForActivationAsync(Now, Arg.Any<CancellationToken>());
        await _contracts.Received(1).GetDueForCompletionAsync(Now, Arg.Any<CancellationToken>());
    }
}
