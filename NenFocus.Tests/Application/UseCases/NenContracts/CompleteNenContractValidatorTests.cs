using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
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
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours =
        Substitute.For<IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public CompleteNenContractValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    private CompleteNenContractValidator CreateValidator() => new(_contracts, _blocks, _completedHours, _clock);

    private static NenContract ActiveContract(double committedHours = 4) => NenContract.Seal(
        id: ContractId, name: "Focused Week", goal: new NenContractGoal("Ship it"),
        startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.Locked,
        dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
        sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private void StubCompleted(double hours) =>
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = hours });

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
        contract.Break(Now);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NotAllHoursCompletedYet_IsInvalid()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(3);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_AllHoursCompletedNoPendingBlocks_IsValid()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OverDelivered_StillValid()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(6);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PendingContractLinkedBlockInFuture_IsInvalid()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var pending = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: ContractId);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pending]);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PendingBonusBlockWithinContractRange_IsInvalid()
    {
        // No ContractId at all — still blocks completion per the architect's own
        // rule ("if there's still a bonus block, the button doesn't appear").
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var bonusDay = StartDate.AddDays(3).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var pendingBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), bonusDay, bonusDay.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pendingBonus]);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_AlreadyEndedContractLinkedBlock_DoesNotBlock()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var ended = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-2), contractId: ContractId);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[ended]);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_CancelledPendingBlock_DoesNotBlock()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var cancelled = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: ContractId);
        cancelled.MarkCancelled(Now);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[cancelled]);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BonusBlockOutsideContractRange_DoesNotBlock()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var outsideDay = EndDate.AddDays(5).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var outsideBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), outsideDay, outsideDay.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[outsideBonus]);

        var result = await CreateValidator().ValidateAsync(new CompleteNenContractCommand { ContractId = ContractId });

        result.IsValid.Should().BeTrue();
    }
}
