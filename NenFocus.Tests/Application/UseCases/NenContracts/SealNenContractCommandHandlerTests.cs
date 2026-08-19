using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class SealNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IPlannedFocusHoursCalculator _calculator = Substitute.For<IPlannedFocusHoursCalculator>();
    private readonly IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool> _checkOverlap =
        Substitute.For<IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public SealNenContractCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _checkOverlap.HandleAsync(Arg.Any<CheckFocusBlockBatchOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    // Real IContractBlockBatchPreparer, composed with the mocked calculator — the
    // preparer itself is pure domain logic (construction + in-batch overlap check), so
    // there's nothing worth mocking about it; only its one IO-touching dependency
    // (the calculator) needs stubbing per test.
    private SealNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, new ContractBlockBatchPreparer(_calculator), _checkOverlap, _clock);

    private static SealNenContractCommand BaseCommand(
        double? fixedFloor = null,
        IReadOnlyList<DateOnly>? restDays = null,
        IReadOnlyList<NenContractBlockSpec>? blocks = null) => new()
    {
        Name = "Focused Week",
        Goal = "Ship it",
        StartDate = StartDate,
        EndDate = EndDate,
        StrictnessRule = StrictnessRule.Locked,
        AllowCancellation = false,
        FixedDailyFloorHours = fixedFloor,
        RestDays = restDays ?? [],
        Blocks = blocks ?? []
    };

    [Fact]
    public async Task HandleAsync_SimpleContract_DerivesFloorFromPlannedHoursAndSucceeds()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0), IsFocus: true);

        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 2 });

        var result = await CreateHandler().HandleAsync(BaseCommand(blocks: [block]), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContractId.Should().NotBeNull();
        await _contracts.Received(1).AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdvancedContract_UsesFixedFloorWithRestDayExceptions()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>()); // no blocks needed for this check

        var restDay = StartDate.AddDays(2);
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0, restDays: [restDay]), CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.DailyFloorPolicy.Default!.Hours.Should().Be(0);
        saved.DailyFloorPolicy.PerDayExceptions.Should().ContainKey(restDay).WhoseValue.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_OverlappingFocusBlocksInBatch_FailsWithoutPersisting()
    {
        var first = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0), IsFocus: true);
        var overlapping = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0, blocks: [first, overlapping]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlockOverlapsAlreadyPersistedBlock_FailsWithoutPersisting()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0), IsFocus: true);
        _checkOverlap.HandleAsync(Arg.Any<CheckFocusBlockBatchOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0, blocks: [block]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DomainSealRejectsBelowFloorDay_FailsWithoutPersisting()
    {
        // A fixed floor of 4h with a day planned below it — Seal() throws internally.
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 4), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("below the daily floor");
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractPresetIdGiven_PassedThroughToSealedContract()
    {
        var presetId = Guid.NewGuid();
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0) with { ContractPresetId = presetId }, CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ContractPresetId.Should().Be(presetId);
    }

    [Fact]
    public async Task HandleAsync_NoWorkHours_SealedContractHasNullWorkHoursPolicy()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(BaseCommand(fixedFloor: 0), CancellationToken.None);

        saved!.WorkHoursPolicy.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WorkHoursGiven_SealedContractHasMatchingWorkHoursPolicy()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 0) with
        {
            WorkHoursStart = new TimeOnly(9, 0),
            WorkHoursEnd = new TimeOnly(17, 0)
        };

        await CreateHandler().HandleAsync(command, CancellationToken.None);

        saved!.WorkHoursPolicy.Should().NotBeNull();
        saved.WorkHoursPolicy!.Default!.Start.Should().Be(new TimeOnly(9, 0));
        saved.WorkHoursPolicy.Default!.End.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public async Task HandleAsync_NoWorkHoursDays_AppliedAsNoConstraintExceptions()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var restDay = StartDate.AddDays(2);
        var command = BaseCommand(fixedFloor: 0) with
        {
            WorkHoursStart = new TimeOnly(9, 0),
            WorkHoursEnd = new TimeOnly(17, 0),
            NoWorkHoursDays = [restDay]
        };

        await CreateHandler().HandleAsync(command, CancellationToken.None);

        saved!.WorkHoursPolicy!.For(restDay).Should().BeNull();
        saved.WorkHoursPolicy.For(StartDate)!.Start.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task HandleAsync_MixedFocusAndBackgroundBlocks_OnlyFocusRangesFeedTheCalculator()
    {
        var focus = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0), IsFocus: true);
        var background = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 12, 0, 0), new DateTime(2026, 1, 1, 13, 0, 0), IsFocus: false);

        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });

        await CreateHandler().HandleAsync(BaseCommand(blocks: [focus, background]), CancellationToken.None);

        _calculator.Received(1).Calculate(Arg.Is<IEnumerable<(DateTime Start, DateTime End)>>(r =>
            r.Count() == 1 && r.First().Start == focus.Start));
        await _blocks.Received(2).AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PersistedBlocks_AreLinkedToTheNewContractId()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0), IsFocus: true);

        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(BaseCommand(blocks: [block]), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ContractId.Should().Be(result.ContractId);
    }

    // ── Period Time Budget: no fixed floor -> ContractFloor ─────────────────────

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetNoFixedFloor_DerivesContractFloorFromTotalPlannedHours()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0), IsFocus: true);
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 3, [StartDate.AddDays(1)] = 4, [StartDate.AddDays(2)] = 2 });
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(blocks: [block]) with { StrictnessRule = StrictnessRule.PeriodTimeBudget, FixedDailyFloorHours = null };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ContractFloorHours.Should().Be(9); // 3 + 4 + 2
        saved.DailyFloorPolicy.Default.Should().BeNull();
        saved.DailyFloorPolicy.PerDayExceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetWithFixedFloor_UsesFixedDailyFloorPolicy_NotContractFloor()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 0) with { StrictnessRule = StrictnessRule.PeriodTimeBudget };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ContractFloorHours.Should().BeNull();
        saved.DailyFloorPolicy.Default!.Hours.Should().Be(0);
    }
}
