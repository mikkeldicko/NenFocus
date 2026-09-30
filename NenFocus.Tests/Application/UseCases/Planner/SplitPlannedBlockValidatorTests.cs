using FluentAssertions;
using TestAt = NenFocus.Tests.At;
using NenFocus.Domain.Services;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class SplitPlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    // Original block: 14:00-17:00 UTC, well into the future relative to Now.
    private static readonly DateTime OriginalStart = new(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OriginalEnd = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _computePlannedFocusHours =
        Substitute.For<IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();

    public SplitPlannedBlockValidatorTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        // Default: no planned hours anywhere — combined with DailyFloor.None (every
        // existing SealedContract fixture below), the floor check trivially passes
        // (0 >= 0) unless a test configures a real floor and a real result.
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>());
    }

    private SplitPlannedBlockValidator CreateValidator() => new(_blocks, _sessions, _contracts, _clock, _computePlannedFocusHours, new BlockPlacementChecker());

    private static SplitPlannedBlockCommand ValidCommand(Guid blockId) => new()
    {
        OriginalBlockId = blockId,
        First = At.Slot(OriginalStart, OriginalStart.AddHours(2)),
            Second = At.Slot(OriginalStart.AddHours(2), OriginalEnd)
    };

    private static NenContract SealedContract(StrictnessRule rule, Guid id) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: TestAt.Utc(Now.Date), timeZone: TestAt.UtcZone);

    private void SetUpDailyFlexBlock(Guid blockId, Guid contractId)
    {
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyOriginalBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockNotFound_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockHasNoContract_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd)));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockdownContract_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.Locked, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetContract_IsValid()
    {
        // Widened alongside the floor check: Period Time Budget contracts allow
        // splitting just like Daily Flexibility does — only Lockdown is immutable.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_CancelledBlock_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId);
        block.MarkCancelled(TestAt.Utc(Now));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockHasSession_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        _sessions.GetByOriginatingBlockIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PastBlock_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var pastStart = Now.AddHours(-3);
        var pastEnd = Now.AddHours(-1);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(pastStart, pastEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(pastStart, pastStart.AddHours(1)),
            Second = At.Slot(pastStart.AddHours(1), pastEnd)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SplitResultsOnDifferentDay_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        var command = ValidCommand(blockId) with
        {
            Second = At.Slot(OriginalStart.AddHours(2).AddDays(1), OriginalEnd.AddDays(1))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ResultingBlocksOverlapEachOther_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        // First: 14:00-16:00, Second: 15:00-17:00 — overlaps First by an hour.
        var command = ValidCommand(blockId) with
        {
            Second = At.Slot(OriginalStart.AddHours(1), OriginalEnd)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ResultingBlockOverlapsExistingBlock_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        _blocks.GetForRangeAsync(TestAt.Slot(OriginalStart, OriginalStart.AddHours(2)), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalStart.AddHours(2)))]);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_TotalDurationMismatch_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, contractId);
        // Original is 3h (14:00-17:00); split only adds up to 2h30m, dropping 30 minutes.
        var command = ValidCommand(blockId) with { Second = At.Slot(OriginalStart.AddHours(2), OriginalStart.AddHours(2).AddMinutes(30)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SecondBlockEndsExactlyAtMidnight_SameDayIsValid()
    {
        // Original block is 22:00-24:00 so the split's total duration (1h + 1h) still
        // matches it exactly; the second half ends exactly at midnight — no actual
        // presence on the next calendar day.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        DateTime At(double hours) => day.AddHours(hours);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(At(22), At(24)), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = TestAt.Slot(At(22), At(23)),
            Second = TestAt.Slot(At(23), At(24))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlock_IsInvalid()
    {
        // Split is Focus-only — a Background block can never be split, regardless of how
        // valid the rest of the command would otherwise be.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CrossMidnightOriginal_PiecesOnEitherDay_IsValid()
    {
        // Original: Day1 23:00 - Day2 01:00 (2h). Reallocated as First on Day1, Second on
        // Day2 — proves Split now lets pieces land on either day the original spans,
        // rather than forcing everything onto the start day alone.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day2 = day1.AddDays(1);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(23), day2.AddHours(1)), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(23), day1.AddHours(23).AddMinutes(30)), // 30 min, entirely Day1
            Second = At.Slot(day2, day2.AddHours(1).AddMinutes(30)), // 90 min, entirely Day2
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_CrossMidnightOriginal_BothPiecesOnSameDay_NoRealFloorConfigured_IsValid()
    {
        // Both pieces land on Day1, abandoning Day2 entirely. Uses the zero-floor
        // SealedContract fixture deliberately, to isolate day-membership behavior from
        // floor behavior — see the floor-check tests below for what happens when a real
        // floor is actually configured and would be violated.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day2 = day1.AddDays(1);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(23), day2.AddHours(1)), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(10), day1.AddHours(11)),
            Second = At.Slot(day1.AddHours(11), day1.AddHours(12))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    // Date range narrowed to just the two days these tests care about (rather than
    // SealedContract's open-ended 30-day range) — Seal() itself hard-validates every day
    // in range against the floor, so a wide range would need a satisfying planned-hours
    // entry for every single day. The seal-time snapshot below only needs to satisfy
    // Seal's own check; it's independent of whatever the validator's own floor check
    // (mocked via _computePlannedFocusHours) reports as the live, post-split state.
    private static NenContract SealedContractWithFloor(Guid id, double floorHours, DateOnly day1, DateOnly day2) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(day1, day2, new DailyFloorRule.Fixed(new DailyFloor(floorHours))),
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(floorHours)),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [day1] = floorHours, [day2] = floorHours },
            sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone);

    [Fact]
    public async Task Validate_CrossMidnightOriginal_BothPiecesOnSameDay_LeavesTheOtherDayBelowItsFloor_IsInvalid()
    {
        // A real floor this time (2h) — abandoning Day2 entirely now leaves it with 0
        // planned hours, below the floor. This is exactly the case the floor check exists
        // for: same-day splits can never violate the floor (the "must sum to the original
        // duration" rule already conserves a single day's total), but a cross-midnight
        // original genuinely can be reallocated unevenly across the two days it spans.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day2 = day1.AddDays(1);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(23), day2.AddHours(1)), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(contractId, floorHours: 2, DateOnly.FromDateTime(day1), DateOnly.FromDateTime(day2)));
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(day1)] = 2 }); // Day2 absent = 0 planned
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(10), day1.AddHours(11)),
            Second = At.Slot(day1.AddHours(11), day1.AddHours(12))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CrossMidnightOriginal_PiecesOnEitherDay_BothDaysMeetTheFloor_IsValid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day2 = day1.AddDays(1);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(23), day2.AddHours(1)), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(contractId, floorHours: 0.5, DateOnly.FromDateTime(day1), DateOnly.FromDateTime(day2)));
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>
            {
                [DateOnly.FromDateTime(day1)] = 0.5,
                [DateOnly.FromDateTime(day2)] = 1.5
            });
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(23), day1.AddHours(23).AddMinutes(30)), // 30 min, entirely Day1
            Second = At.Slot(day2, day2.AddHours(1).AddMinutes(30)), // 90 min, entirely Day2
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    // Same narrowed-range rationale as SealedContractWithFloor above; DailyFloorPolicy
    // stays DailyFloor.None (Seal forbids a contract carrying both a daily floor and a
    // whole-contract ContractFloor at once).
    private static NenContract SealedPeriodBudgetContract(Guid id, double contractFloorHours, DateOnly day1, DateOnly day2) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.PeriodTimeBudget,
            calendar: TestCalendars.For(day1, day2, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: TestAt.Utc(day1.ToDateTime(TimeOnly.MinValue)), timeZone: TestAt.UtcZone,
            contractFloor: new ContractFloor(contractFloorHours));

    [Fact]
    public async Task Validate_PeriodTimeBudgetWithContractFloor_DropsBelowPeriodTotal_IsInvalid()
    {
        // A whole-period ContractFloor checks the sum across the period, not any one day —
        // reallocating hours between the two pieces can still legally drop the period total
        // if the split itself sheds time (e.g. a gap left unaccounted for isn't possible
        // here since duration must sum to the original, but ComputePlannedFocusHours also
        // reflects every OTHER block in the period, which this test starves down directly).
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = DateOnly.FromDateTime(OriginalStart);
        SetUpDailyFlexBlock(blockId, contractId); // reuse for block wiring only
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedPeriodBudgetContract(contractId, contractFloorHours: 10, day1, day1.AddDays(30)));
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [day1] = 3 }); // well under the 10h period floor

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetWithContractFloor_MeetsPeriodTotal_IsValid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = DateOnly.FromDateTime(OriginalStart);
        SetUpDailyFlexBlock(blockId, contractId);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedPeriodBudgetContract(contractId, contractFloorHours: 3, day1, day1.AddDays(30)));
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [day1] = 3 });

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetWithoutContractFloor_FallsBackToPerDayFloor_IsInvalid()
    {
        // No ContractFloor set — falls back to the same per-day IsDayMet check Daily
        // Flexibility uses, per the user-confirmed rule ("if it's period budget without a
        // daily floor it should just check against the floor").
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateOnly(2026, 1, 3);
        var day2 = day1.AddDays(1);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.ToDateTime(TimeOnly.MinValue).AddHours(23), day2.ToDateTime(TimeOnly.MinValue).AddHours(1)), contractId: contractId));
        // The contract's own DailyFloorPolicy carries a real per-day floor (2h) even though
        // StrictnessRule is PeriodTimeBudget — this is the "hybrid" shape: no ContractFloor,
        // so the validator's live check falls back to per-day IsDayMet, same as Daily Flex.
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.PeriodTimeBudget,
                calendar: TestCalendars.For(day1, day2, new DailyFloorRule.Fixed(new DailyFloor(2))),
                floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
                changeWindow: null,
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [day1] = 2, [day2] = 2 },
                sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone));
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [day1] = 2 }); // Day2 abandoned entirely = 0

        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.ToDateTime(TimeOnly.MinValue).AddHours(10), day1.ToDateTime(TimeOnly.MinValue).AddHours(11)),
            Second = At.Slot(day1.ToDateTime(TimeOnly.MinValue).AddHours(11), day1.ToDateTime(TimeOnly.MinValue).AddHours(12))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FirstBlockOutsideWorkHours_IsInvalid()
    {
        // Work hours 15:00-18:00 — First (14:00-16:00, from ValidCommand's 2h split point)
        // starts an hour before the window opens.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.Fixed(DailyFloor.None), workHours: new WorkHours(new TimeOnly(15, 0), new TimeOnly(18, 0))),
                floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
                changeWindow: null,
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SecondBlockOutsideWorkHours_IsInvalid()
    {
        // Work hours 9:00-16:00 — Second (16:00-17:00, from ValidCommand's 2h split point)
        // ends an hour after the window closes.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.Fixed(DailyFloor.None), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(16, 0))),
                floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
                changeWindow: null,
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BothBlocksWithinWorkHours_IsValid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), contractId: contractId));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.Fixed(DailyFloor.None), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0))),
                floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
                changeWindow: null,
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PieceItselfCrossesMidnight_IsInvalid()
    {
        // Each resulting piece must resolve to a single day — no recursive crossing.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day2 = day1.AddDays(1);

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(22), day2.AddHours(2)), contractId: contractId)); // 4h
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(22), day2.AddHours(1)), // crosses midnight itself
            Second = At.Slot(day2.AddHours(1), day2.AddHours(2))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ResultingBlockOverlapsRunningSession_IsInvalid()
    {
        // Gap 88: Split now respects a running session too (BlockPlacementChecker.CheckFree).
        var blockId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, Guid.NewGuid());
        var running = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        running.Start(TestAt.Utc(OriginalStart.AddMinutes(30)));
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[running]);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.Errors.Should().Contain(e => e.ErrorMessage == "First block: A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task Validate_ResultingBlockInThePast_IsInvalid()
    {
        // Gap 88 phase 8: the original hasn't started, but a half may still be moved to an
        // earlier hour of the same day — one that has already passed (Now is 08:00).
        var blockId = Guid.NewGuid();
        SetUpDailyFlexBlock(blockId, Guid.NewGuid());
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(OriginalStart.AddHours(-8), OriginalStart.AddHours(-7)), // 06:00-07:00
            Second = At.Slot(OriginalStart.AddHours(1), OriginalEnd)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().Contain(e => e.ErrorMessage == "First block: Start time must be in the future.");
    }
}