using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class DeleteNenContractCommandHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly EndDate = new(2026, 6, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();

    private DeleteNenContractCommandHandler CreateHandler() => new(_contracts, _blocks);

    private static NenContract ScheduledContract() => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Summer Focus",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.AddDays(-10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_FailsWithoutTouchingBlocks()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new DeleteNenContractCommand { ContractId = Guid.NewGuid() }, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contracts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ScheduledContract_DeletesContractAndAllLinkedBlocks()
    {
        var contract = ScheduledContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var block1 = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), membership: TestContracts.Committed(contract.Id));
        var block2 = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(11, 0)), StartDate.ToDateTime(new TimeOnly(12, 0))), membership: TestContracts.Committed(contract.Id));
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block1, block2]);

        var result = await CreateHandler().HandleAsync(
            new DeleteNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _blocks.Received(1).DeleteAsync(block1.Id, Arg.Any<CancellationToken>());
        await _blocks.Received(1).DeleteAsync(block2.Id, Arg.Any<CancellationToken>());
        await _contracts.Received(1).DeleteAsync(contract.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BonusBlock_IsReleasedToAPlainBlock_NotDeleted()
    {
        // Gap 97: a Bonus block was added on its own, not drafted — deleting the contract
        // never destroys it.
        var contract = ScheduledContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var drafted = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), membership: TestContracts.Committed(contract.Id));
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(13, 0)), StartDate.ToDateTime(new TimeOnly(14, 0))), membership: TestContracts.Bonus(contract.Id));
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[drafted, bonus]);

        await CreateHandler().HandleAsync(new DeleteNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        await _blocks.Received(1).DeleteAsync(drafted.Id, Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().DeleteAsync(bonus.Id, Arg.Any<CancellationToken>());
        bonus.Membership.Should().BeNull();
        await _blocks.Received(1).UpdateAsync(bonus, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoLinkedBlocks_DeletesJustTheContract()
    {
        var contract = ScheduledContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(
            new DeleteNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contracts.Received(1).DeleteAsync(contract.Id, Arg.Any<CancellationToken>());
    }
}
