using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;
using NenFocus.Application.UseCases.NenContracts.Extend;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class ExtendNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateOnly NewEndDate = new(2026, 1, 9);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IPlannedFocusHoursCalculator _calculator = Substitute.For<IPlannedFocusHoursCalculator>();
    private readonly IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool> _checkOverlap =
        Substitute.For<IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public ExtendNenContractCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>());
        _checkOverlap.HandleAsync(Arg.Any<CheckFocusBlockBatchOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private ExtendNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, new ContractBlockBatchPreparer(_calculator), _checkOverlap, _clock);

    private static NenContract AdvancedActiveContract(double fixedFloor = 0)
    {
        var plan = new Dictionary<DateOnly, double>();
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
            plan[date] = fixedFloor; // satisfies the original range's floor by construction

        return NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            startDate: StartDate,
            endDate: EndDate,
            strictnessRule: StrictnessRule.Locked,
            dailyFloorPolicy: DailyFloorPolicy.Fixed(new DailyFloor(fixedFloor)),
            changeWindow: null,
            allowCancellation: true,
            plannedFocusHoursByDay: plan,
            sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    private static NenContract SimpleActiveContract() => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: StartDate,
        endDate: EndDate,
        strictnessRule: StrictnessRule.Locked,
        dailyFloorPolicy: DailyFloorPolicy.None, // no Default — Simple shape
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_ContractNotFound_FailsWithoutMutating()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = Guid.NewGuid(), NewEndDate = NewEndDate }, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdvancedContract_ExtendsAndAppliesDefaultAutomatically()
    {
        var contract = AdvancedActiveContract(fixedFloor: 0);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate }, CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.EndDate.Should().Be(NewEndDate);
        // No RestDays supplied — new days fall back to the existing Default, no explicit exception needed.
        contract.DailyFloorPolicy.For(EndDate.AddDays(1)).Should().Be(contract.DailyFloorPolicy.Default);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdvancedContract_RestDayBecomesExplicitException()
    {
        var contract = AdvancedActiveContract(fixedFloor: 0);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var restDay = EndDate.AddDays(1);

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate, RestDays = [restDay] },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.DailyFloorPolicy.For(restDay).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_SimpleContract_DerivesFloorFromPlannedHoursForNewDays()
    {
        var contract = SimpleActiveContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var newDay = EndDate.AddDays(1);
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [newDay] = 2 });

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate }, CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.DailyFloorPolicy.For(newDay)!.Hours.Should().Be(2);
        // A new day with no planned hours (and no Default) is a true rest day.
        contract.DailyFloorPolicy.For(NewEndDate).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_OverlappingFocusBlocksInNewBatch_FailsWithoutPersisting()
    {
        var contract = AdvancedActiveContract(fixedFloor: 0);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var first = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 8, 9, 0, 0), new DateTime(2026, 1, 8, 11, 0, 0), IsFocus: true);
        var overlapping = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 8, 10, 0, 0), new DateTime(2026, 1, 8, 12, 0, 0), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate, Blocks = [first, overlapping] },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlockOverlapsAlreadyPersistedBlock_FailsWithoutPersisting()
    {
        var contract = AdvancedActiveContract(fixedFloor: 0);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 8, 9, 0, 0), new DateTime(2026, 1, 8, 11, 0, 0), IsFocus: true);
        _checkOverlap.HandleAsync(Arg.Any<CheckFocusBlockBatchOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate, Blocks = [block] },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NewDayBelowFloor_FailsWithoutPersisting()
    {
        var contract = AdvancedActiveContract(fixedFloor: 4);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [EndDate.AddDays(1)] = 1 }); // below floor of 4

        var result = await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("below the daily floor");
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PersistedBlocks_AreLinkedToTheExistingContractId()
    {
        var contract = AdvancedActiveContract(fixedFloor: 0);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 8, 9, 0, 0), new DateTime(2026, 1, 8, 10, 0, 0), IsFocus: true);

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            new ExtendNenContractCommand { ContractId = contract.Id, NewEndDate = NewEndDate, Blocks = [block] },
            CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ContractId.Should().Be(contract.Id);
    }
}
