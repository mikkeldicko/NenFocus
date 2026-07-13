using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class BreakNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public BreakNenContractCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private BreakNenContractCommandHandler CreateHandler() => new(_contracts, _blocks, _clock);

    private static NenContract ActiveCancellableContract() => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: StartDate,
        endDate: EndDate,
        strictnessRule: StrictnessRule.DailyFlexibility,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_ContractNotFound_FailsWithoutMutating()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new BreakNenContractCommand { ContractId = Guid.NewGuid() }, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidContract_BreaksItAndSaves()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(
            new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.Status.Should().Be(NenContractStatus.Broken);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DeletesOnlyBlocksThatHaveNotEnded()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var pastBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddHours(-3), Now.AddHours(-1), contractId: contract.Id); // already ended
        var currentBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddHours(-1), Now.AddHours(1), contractId: contract.Id); // active right now
        var futureBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddDays(1), Now.AddDays(1).AddHours(1), contractId: contract.Id); // upcoming

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pastBlock, currentBlock, futureBlock]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        await _blocks.DidNotReceive().DeleteAsync(pastBlock.Id, Arg.Any<CancellationToken>());
        await _blocks.Received(1).DeleteAsync(currentBlock.Id, Arg.Any<CancellationToken>());
        await _blocks.Received(1).DeleteAsync(futureBlock.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoLinkedBlocks_DeletesNothing()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
