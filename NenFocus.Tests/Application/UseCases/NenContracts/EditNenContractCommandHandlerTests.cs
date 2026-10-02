using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class EditNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly EndDate = new(2026, 6, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IPlannedFocusHoursCalculator _calculator = Substitute.For<IPlannedFocusHoursCalculator>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public EditNenContractCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
        _blocks.GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
    }

    private EditNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, new ContractBlockBatchPreparer(_calculator), _sessions, new BlockPlacementChecker(), _clock);

    private static NenContract ScheduledContract(Guid id) => TestContracts.Seal(
        id: id,
        name: "Original Name",
        goal: new NenContractGoal("Original goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.AddDays(-10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private static EditNenContractCommand BaseCommand(
        Guid contractId,
        double? fixedFloor = 0,
        IReadOnlyList<NenContractBlockSpec>? blocks = null) => new()
    {
        ContractId = contractId,
        Name = "Renamed Contract",
        Goal = "Updated goal",
        StartDate = StartDate,
        EndDate = EndDate,
        StrictnessRule = StrictnessRule.Locked,
        AllowCancellation = false,
        FixedDailyFloorHours = fixedFloor,
        Blocks = blocks ?? []
    };

    [Fact]
    public async Task HandleAsync_ContractNotFound_FailsWithoutMutating()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(BaseCommand(Guid.NewGuid()), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ActiveContract_FailsWithoutMutating()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        contract.Activate(At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(BaseCommand(contract.Id), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidEdit_DeletesOldAndAddsNewUnderSameId()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(BaseCommand(contract.Id), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContractId.Should().Be(contract.Id);
        await _contracts.Received(1).DeleteAsync(contract.Id, Arg.Any<CancellationToken>());
        await _contracts.Received(1).AddAsync(
            Arg.Is<NenContract>(c => c.Id == contract.Id && c.Name == "Renamed Contract"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidEdit_DeletesEveryOldLinkedBlock()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var oldBlock1 = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), membership: TestContracts.Committed(contract.Id));
        var oldBlock2 = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(11, 0)), StartDate.ToDateTime(new TimeOnly(12, 0))), membership: TestContracts.Committed(contract.Id));
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[oldBlock1, oldBlock2]);

        await CreateHandler().HandleAsync(BaseCommand(contract.Id), CancellationToken.None);

        await _blocks.Received(1).DeleteAsync(oldBlock1.Id, Arg.Any<CancellationToken>());
        await _blocks.Received(1).DeleteAsync(oldBlock2.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidEdit_NewBlocksLinkedToSameContractId()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var newBlockSpec = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), IsFocus: true);
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });

        PlannedBlock? savedBlock = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => savedBlock = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(
            BaseCommand(contract.Id, blocks: [newBlockSpec]), CancellationToken.None);

        result.Success.Should().BeTrue();
        savedBlock.Should().NotBeNull();
        savedBlock!.Membership.Should().Be(TestContracts.Committed(contract.Id));
    }

    [Fact]
    public async Task HandleAsync_OverlapsOnlyWithItsOwnOldBlocks_Succeeds()
    {
        // The new schedule reuses the exact same time slot the old (about-to-be-replaced)
        // blocks occupied — this must NOT be treated as a collision.
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var sameSlotSpec = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), IsFocus: true);

        // The old block, still in the database while the edit runs, is left out of the calendar.
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)
            [FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), membership: TestContracts.Committed(contract.Id))]);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(contract.Id, blocks: [sameSlotSpec]), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_OverlapsWithinNewBatch_FailsWithoutMutating()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var first = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(11, 0))), IsFocus: true);
        var overlapping = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(10, 0)), StartDate.ToDateTime(new TimeOnly(12, 0))), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(contract.Id, blocks: [first, overlapping]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_OverlapsWithAnotherContractsBlock_FailsWithoutMutating()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)
            [FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(9, 30)), StartDate.ToDateTime(new TimeOnly(10, 30))), membership: TestContracts.Committed(Guid.NewGuid()))]);

        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(contract.Id, blocks: [block]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DomainSealRejectsBelowFloorDay_FailsWithoutMutatingAnything()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), membership: TestContracts.Committed(contract.Id))]);
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });

        var result = await CreateHandler().HandleAsync(
            BaseCommand(contract.Id, fixedFloor: 4), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("below the daily floor");
        await _contracts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractPresetIdGiven_PassedThroughToNewContract()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var presetId = Guid.NewGuid();
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            BaseCommand(contract.Id) with { ContractPresetId = presetId }, CancellationToken.None);

        saved!.ContractPresetId.Should().Be(presetId);
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetNoFixedFloor_DerivesContractFloorFromTotalPlannedHours()
    {
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(11, 0))), IsFocus: true);
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 3, [StartDate.AddDays(1)] = 4, [StartDate.AddDays(2)] = 2 });
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(contract.Id, fixedFloor: null, blocks: [block])
            with
        { StrictnessRule = StrictnessRule.PeriodTimeBudget };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ContractFloorHours.Should().Be(9); // 3 + 4 + 2
        saved.FloorRule.Should().Be(new DailyFloorRule.None());
    }

    [Fact]
    public async Task HandleAsync_KeepBlockingDuringBreaks_CarriedIntoTheReSealedContract()
    {
        // Regression (Gap 64, trap 3): editing re-seals the contract, which used to drop this rule.
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(contract.Id) with { KeepBlockingDuringBreaks = true }, CancellationToken.None);

        result.Success.Should().BeTrue();
        await _contracts.Received(1).AddAsync(
            Arg.Is<NenContract>(c => c.KeepBlockingDuringBreaks), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EveryRuleOfTheCommand_IsCarriedIntoTheReSealedContract()
    {
        // The rules travel as one value (Gap 93), so a re-seal can't drop one of them.
        var contract = ScheduledContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(contract.Id) with
        {
            AllowCancellation = true,
            KeepBlockingDuringBreaks = true,
            RedemptionPlacementPolicy = RedemptionPlacementPolicy.CurrentDayOnly,
            BonusBlocksFollowStrictness = true
        };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved!.Rules.Should().Be(new ContractRules(
            command.StrictnessRule,
            command.FixedDailyFloorHours is { } hours ? new DailyFloor(hours) : null,
            allowCancellation: true, keepBlockingDuringBreaks: true,
            RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true));
    }
}
