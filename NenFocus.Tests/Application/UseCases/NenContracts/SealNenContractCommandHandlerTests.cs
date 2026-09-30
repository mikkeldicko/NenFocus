using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
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
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public SealNenContractCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    // Real IContractBlockBatchPreparer, composed with the mocked calculator — the
    // preparer itself is pure domain logic (construction + in-batch overlap check), so
    // there's nothing worth mocking about it; only its one IO-touching dependency
    // (the calculator) needs stubbing per test.
    private SealNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, new ContractBlockBatchPreparer(_calculator), _sessions, new BlockPlacementChecker(), _clock);

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
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);

        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 2 });

        var result = await CreateHandler().HandleAsync(BaseCommand(blocks: [block]), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContractId.Should().NotBeNull();
        await _contracts.Received(1).AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdvancedContract_UsesFixedFloorWithRestDayExceptions()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>()); // no blocks needed for this check

        var restDay = StartDate.AddDays(2);
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0, restDays: [restDay]), CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(0)));
        saved.FloorHoursFor(StartDate).Should().Be(0);
        saved.IsRestDay(restDay).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_OverlappingFocusBlocksInBatch_FailsWithoutPersisting()
    {
        var first = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);
        var overlapping = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0)), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0, blocks: [first, overlapping]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlockOverlapsAlreadyPersistedBlock_FailsWithoutPersisting()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)
            [FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0)))]);

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
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
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
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0) with { ContractPresetId = presetId }, CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ContractPresetId.Should().Be(presetId);
    }

    [Fact]
    public async Task HandleAsync_NoWorkHours_EveryDayIsAFullDay()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(BaseCommand(fixedFloor: 0), CancellationToken.None);

        saved!.WindowOn(StartDate.AddDays(1)).Should().Be((
            StartDate.AddDays(1).ToDateTime(TimeOnly.MinValue), StartDate.AddDays(2).ToDateTime(TimeOnly.MinValue)));
    }

    [Fact]
    public async Task HandleAsync_WorkHoursGiven_EveryDayIsTheWorkHoursWindow()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 0) with
        {
            WorkHoursStart = new TimeOnly(9, 0),
            WorkHoursEnd = new TimeOnly(17, 0)
        };

        await CreateHandler().HandleAsync(command, CancellationToken.None);

        saved!.StartsAt.Should().Be(StartDate.ToDateTime(new TimeOnly(9, 0)));
        saved.EndsAt.Should().Be(EndDate.ToDateTime(new TimeOnly(17, 0)));
    }

    [Fact]
    public async Task HandleAsync_NoWorkHoursDays_AreFullDays()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
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

        saved!.WindowOn(restDay).Should().Be((restDay.ToDateTime(TimeOnly.MinValue), restDay.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        saved.WindowOn(StartDate)!.Value.Start.Should().Be(StartDate.ToDateTime(new TimeOnly(9, 0)));
    }

    [Fact]
    public async Task HandleAsync_MixedFocusAndBackgroundBlocks_OnlyFocusRangesFeedTheCalculator()
    {
        var focus = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), IsFocus: true);
        var background = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 12, 0, 0), new DateTime(2026, 1, 1, 13, 0, 0)), IsFocus: false);

        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });

        await CreateHandler().HandleAsync(BaseCommand(blocks: [focus, background]), CancellationToken.None);

        _calculator.Received(1).Calculate(
            Arg.Is<IEnumerable<TimeSlot>>(r => r.SequenceEqual(new[] { focus.Slot })),
            Arg.Any<UserTimeZone>());
        await _blocks.Received(2).AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PersistedBlocks_AreLinkedToTheNewContractId()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), IsFocus: true);

        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
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
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 3, [StartDate.AddDays(1)] = 4, [StartDate.AddDays(2)] = 2 });
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(blocks: [block]) with { StrictnessRule = StrictnessRule.PeriodTimeBudget, FixedDailyFloorHours = null };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ContractFloorHours.Should().Be(9); // 3 + 4 + 2
        saved.FloorRule.Should().Be(new DailyFloorRule.None());
        saved.IsRestDay(StartDate).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetWithFixedFloor_UsesAFixedDailyFloor_NotContractFloor()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 0) with { StrictnessRule = StrictnessRule.PeriodTimeBudget };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ContractFloorHours.Should().BeNull();
        saved.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(0)));
    }

    [Fact]
    public async Task HandleAsync_KeepBlockingDuringBreaks_CopiedOntoSealedContract()
    {
        // Regression (Gap 64, trap 2): Seal was never given this rule, so every contract
        // was sealed with breaks lifting blocking whatever its preset said.
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0) with { KeepBlockingDuringBreaks = true }, CancellationToken.None);

        saved!.KeepBlockingDuringBreaks.Should().BeTrue();
    }
// ── Gap 65: start moment, block placement ──────────────────────────────────

    [Fact]
    public async Task HandleAsync_StartDateAlreadyPast_Fails()
    {
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        var command = BaseCommand(fixedFloor: 0) with { StartDate = StartDate.AddDays(-1) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("start time has passed");
    }

    [Fact]
    public async Task HandleAsync_StartsTodayAfterWorkHoursOpened_StartsAtTheCurrentMinute()
    {
        _clock.SetNow(StartDate.ToDateTime(new TimeOnly(12, 0, 40)));
        var planned = Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .ToDictionary(i => StartDate.AddDays(i), _ => 4.0); // meets each day's floor
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>()).Returns(planned);
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 4) with { WorkHoursStart = new TimeOnly(9, 0), WorkHoursEnd = new TimeOnly(17, 0) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved!.StartsAt.Should().Be(StartDate.ToDateTime(new TimeOnly(12, 0)));
        saved.Status.Should().Be(NenContractStatus.Active);
        saved.FloorHoursFor(StartDate).Should().Be(4); // the full floor — 5h are left, never shrunk
    }

    [Fact]
    public async Task HandleAsync_StartsTodayWithLessTimeLeftThanTheFloor_Fails()
    {
        // The reported case: 16:38 with Work Hours until 18:00 — 1h22m left for a 2h floor.
        _clock.SetNow(StartDate.ToDateTime(new TimeOnly(16, 38)));
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double>());
        var command = BaseCommand(fixedFloor: 2) with { WorkHoursStart = new TimeOnly(9, 0), WorkHoursEnd = new TimeOnly(18, 0) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Not enough time left on");
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlockOutsideItsDaysWindow_Fails()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 18, 0, 0), new DateTime(2026, 1, 1, 19, 0, 0)), IsFocus: true);
        _calculator.Calculate(Arg.Any<IEnumerable<TimeSlot>>(), Arg.Any<UserTimeZone>())
            .Returns(new Dictionary<DateOnly, double> { [StartDate] = 1 });
        var command = BaseCommand(blocks: [block]) with { WorkHoursStart = new TimeOnly(9, 0), WorkHoursEnd = new TimeOnly(17, 0) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlockOverlapsRunningSession_FailsWithoutPersisting()
    {
        // Gap 88: Seal now respects a running session too (BlockPlacementChecker.CheckFree).
        var running = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        running.Start(At.Utc(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)));
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[running]);
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 0, blocks: [block]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }
}