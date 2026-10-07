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
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public SealNenContractCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    // Real IContractBlockBatchPreparer — pure domain logic (construction, in-batch overlap check,
    // planned hours per day), so each test's planned hours are simply its blocks.
    private SealNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, new ContractBlockBatchPreparer(), _sessions, new BlockPlacementChecker(), _clock);

    /// <summary>A 1h Focus block (09:00–10:00) on every contract day but <paramref name="restDays"/> — meets a 1h fixed floor.</summary>
    private static IReadOnlyList<NenContractBlockSpec> OneHourEveryDayExcept(params DateOnly[] restDays) =>
        Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(i => StartDate.AddDays(i))
            .Where(day => !restDays.Contains(day))
            .Select(day => new NenContractBlockSpec(Guid.NewGuid(),
                At.Slot(day.ToDateTime(new TimeOnly(9, 0)), day.ToDateTime(new TimeOnly(10, 0))), IsFocus: true))
            .ToList();

    private static SealNenContractCommand BaseCommand(
        int? fixedFloor = null,
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


        var result = await CreateHandler().HandleAsync(BaseCommand(blocks: [block]), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContractId.Should().NotBeNull();
        await _contracts.Received(1).AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdvancedContract_UsesFixedFloorWithRestDayExceptions()
    {
        var restDay = StartDate.AddDays(2);
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(
            BaseCommand(fixedFloor: 1, restDays: [restDay], blocks: OneHourEveryDayExcept(restDay)), CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(1)));
        saved.FloorHoursFor(StartDate).Should().Be(1);
        saved.FloorHoursFor(restDay).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_OverlappingFocusBlocksInBatch_FailsWithoutPersisting()
    {
        var first = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);
        var overlapping = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0)), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(blocks: [first, overlapping]), CancellationToken.None);

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
            BaseCommand(blocks: [block]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DomainSealRejectsBelowFloorDay_FailsWithoutPersisting()
    {
        // A fixed floor of 4h with a day planned below it — Seal() throws internally.

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
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            BaseCommand() with { ContractPresetId = presetId }, CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ContractPresetId.Should().Be(presetId);
    }

    [Fact]
    public async Task HandleAsync_NoWorkHours_EveryDayIsAFullDay()
    {
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(BaseCommand(), CancellationToken.None);

        saved!.WindowOn(StartDate.AddDays(1)).Should().Be((
            StartDate.AddDays(1).ToDateTime(TimeOnly.MinValue), StartDate.AddDays(2).ToDateTime(TimeOnly.MinValue)));
    }

    [Fact]
    public async Task HandleAsync_WorkHoursGiven_EveryDayIsTheWorkHoursWindow()
    {
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand() with
        {
            WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0))
        };

        await CreateHandler().HandleAsync(command, CancellationToken.None);

        saved!.StartsAt.Should().Be(StartDate.ToDateTime(new TimeOnly(9, 0)));
        saved.EndsAt.Should().Be(EndDate.ToDateTime(new TimeOnly(17, 0)));
    }

    [Fact]
    public async Task HandleAsync_NoWorkHoursDays_AreFullDays()
    {
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var restDay = StartDate.AddDays(2);
        var command = BaseCommand() with
        {
            WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            NoWorkHoursDays = [restDay]
        };

        await CreateHandler().HandleAsync(command, CancellationToken.None);

        saved!.WindowOn(restDay).Should().Be((restDay.ToDateTime(TimeOnly.MinValue), restDay.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        saved.WindowOn(StartDate)!.Value.Start.Should().Be(StartDate.ToDateTime(new TimeOnly(9, 0)));
    }

    [Fact]
    public async Task HandleAsync_PersistedBlocks_AreLinkedToTheNewContractId()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), IsFocus: true);


        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(BaseCommand(blocks: [block]), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Membership.Should().Be(TestContracts.Committed(result.ContractId!.Value));
    }

    // ── Period Time Budget: no fixed floor -> a whole-period target ─────────────────────

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetNoFixedFloor_TheTargetIsTheTotalPlannedHours()
    {
        NenContractBlockSpec Focus(int day, int fromHour, int toHour) => new(Guid.NewGuid(),
            At.Slot(new DateTime(2026, 1, day, fromHour, 0, 0), new DateTime(2026, 1, day, toHour, 0, 0)), IsFocus: true);
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(blocks: [Focus(1, 9, 12), Focus(2, 9, 13), Focus(3, 9, 11)])
            with { StrictnessRule = StrictnessRule.PeriodTimeBudget, FixedDailyFloorHours = null };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.HasPeriodTarget.Should().BeTrue();
        saved.CommittedFocusHours.Should().Be(9); // 3 + 4 + 2 — the whole-period target
        saved.FloorRule.Should().Be(new DailyFloorRule.None());
        saved.FloorHoursFor(StartDate).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetWithFixedFloor_UsesAFixedDailyFloor_NoPeriodTarget()
    {
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 1, blocks: OneHourEveryDayExcept()) with { StrictnessRule = StrictnessRule.PeriodTimeBudget };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.HasPeriodTarget.Should().BeFalse();
        saved.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(1)));
    }

    [Fact]
    public async Task HandleAsync_KeepBlockingDuringBreaks_CopiedOntoSealedContract()
    {
        // Regression (Gap 64, trap 2): Seal was never given this rule, so every contract
        // was sealed with breaks lifting blocking whatever its preset said.
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(
            BaseCommand() with { KeepBlockingDuringBreaks = true }, CancellationToken.None);

        saved!.KeepBlockingDuringBreaks.Should().BeTrue();
    }
// ── Gap 65: start moment, block placement ──────────────────────────────────

    [Fact]
    public async Task HandleAsync_StartDateAlreadyPast_Fails()
    {
        var command = BaseCommand() with { StartDate = StartDate.AddDays(-1) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("start time has passed");
    }

    [Fact]
    public async Task HandleAsync_StartsTodayAfterWorkHoursOpened_StartsAtTheCurrentMinute()
    {
        _clock.SetNow(StartDate.ToDateTime(new TimeOnly(12, 0, 40)));
        // 4h a day meets each day's floor; today's block starts after now, inside the hours left.
        var blocks = Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(i =>
            {
                var from = StartDate.AddDays(i).ToDateTime(i == 0 ? new TimeOnly(12, 30) : new TimeOnly(9, 0));
                return new NenContractBlockSpec(Guid.NewGuid(), At.Slot(from, from.AddHours(4)), IsFocus: true);
            })
            .ToList();
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand(fixedFloor: 4, blocks: blocks) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)) };

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
        var command = BaseCommand(fixedFloor: 2) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0)) };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Not enough time left on");
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlockOutsideItsDaysWindow_Fails()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 18, 0, 0), new DateTime(2026, 1, 1, 19, 0, 0)), IsFocus: true);
        var command = BaseCommand(blocks: [block]) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)) };

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
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(running);
        var block = new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)), IsFocus: true);

        var result = await CreateHandler().HandleAsync(
            BaseCommand(blocks: [block]), CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().AddAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EveryRuleOfTheCommand_IsCarriedIntoTheContract()
    {
        // The rules travel as one value (Gap 93), so Seal can't drop one of them.
        NenContract? saved = null;
        await _contracts.AddAsync(Arg.Do<NenContract>(c => saved = c), Arg.Any<CancellationToken>());
        var command = BaseCommand() with
        {
            StrictnessRule = StrictnessRule.DailyFlexibility,
            AllowCancellation = true,
            KeepBlockingDuringBreaks = true,
            RedemptionPlacementPolicy = RedemptionPlacementPolicy.CurrentDayOnly,
            BonusBlocksFollowStrictness = true
        };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        saved!.Rules.Should().Be(new ContractRules(
            StrictnessRule.DailyFlexibility, fixedDailyFloor: null, allowCancellation: true, keepBlockingDuringBreaks: true,
            RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true));
    }
}