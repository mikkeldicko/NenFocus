using FluentAssertions;
using TestAt = NenFocus.Tests.At;
using NenFocus.Domain.Services;
using NenFocus.Application.Interfaces;
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
    // The real permission answer (Gap 93) — a Domain service with no I/O; its rules are tested on
    // NenContract, PlannedBlock and BlockChangeChecker. Here: that the validator asks it with the
    // contract's blocks as they stand, and passes its refusal on.
    private readonly BlockChangeChecker _changes = new BlockChangeChecker();

    public SplitPlannedBlockValidatorTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        // Default: the contract has no other blocks — combined with no fixed floor (every
        // SealedContract fixture below), the floor check trivially passes unless a test
        // configures a real floor and real blocks.
        _blocks.GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);
    }

    private SplitPlannedBlockValidator CreateValidator() => new(_blocks, _sessions, _contracts, _clock, _changes, new BlockPlacementChecker());

    private static SplitPlannedBlockCommand ValidCommand(Guid blockId) => new()
    {
        OriginalBlockId = blockId,
        First = At.Slot(OriginalStart, OriginalStart.AddHours(2)),
            Second = At.Slot(OriginalStart.AddHours(2), OriginalEnd)
    };

    private static NenContract SealedContract(StrictnessRule rule, Guid id) =>
        TestContracts.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: TestAt.Utc(Now.Date), timeZone: TestAt.UtcZone);

    private void SetUpDailyFlexBlock(Guid blockId, Guid contractId)
    {
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
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
        var block = FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId));
        block.MarkCancelled(TestAt.Utc(Now));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));

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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(pastStart, pastEnd), membership: TestContracts.Committed(contractId)));
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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(At(22), At(24)), membership: TestContracts.Committed(contractId)));
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
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(23), day2.AddHours(1)), membership: TestContracts.Committed(contractId)));
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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(23), day2.AddHours(1)), membership: TestContracts.Committed(contractId)));
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

    // ── Floor protection: the real checker over the contract's real blocks ─────

    private static NenContract SealedContractWithFloor(Guid id, StrictnessRule rule, double floorHours, DateOnly day1, DateOnly day2) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            rules: TestRules.Of(rule, new DailyFloor(floorHours)),
            calendar: TestCalendars.For(day1, day2, new DailyFloorRule.Fixed(new DailyFloor(floorHours))),
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [day1] = floorHours, [day2] = floorHours },
            sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone);

    /// <summary>
    /// A committed block across midnight, 23:00 on day 1 to 01:00 on day 2 — one hour on each day —
    /// in a two-day contract with <paramref name="floorHours"/> a day. With a floor above one hour,
    /// a second committed hour sits on each day so the plan meets it before the split.
    /// </summary>
    private (Guid BlockId, DateTime Day1, DateTime Day2) CrossMidnightBlockUnder(StrictnessRule rule, double floorHours)
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day2 = day1.AddDays(1);
        FocusBlock Committed(Guid id, DateTime from, DateTime to) =>
            FocusBlock.Create(id, Guid.NewGuid(), TestAt.Slot(from, to), membership: TestContracts.Committed(contractId));

        var block = Committed(blockId, day1.AddHours(23), day2.AddHours(1));
        var members = new List<PlannedBlock> { block };
        if (floorHours > 1)
        {
            members.Add(Committed(Guid.NewGuid(), day1.AddHours(8), day1.AddHours(9)));
            members.Add(Committed(Guid.NewGuid(), day2.AddHours(8), day2.AddHours(9)));
        }
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _blocks.GetForContractAsync(contractId, Arg.Any<CancellationToken>()).Returns(members);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(contractId, rule, floorHours, DateOnly.FromDateTime(day1), DateOnly.FromDateTime(day2)));
        return (blockId, day1, day2);
    }

    [Theory]
    [InlineData(StrictnessRule.DailyFlexibility)]
    [InlineData(StrictnessRule.PeriodTimeBudget)]
    public async Task Validate_CrossMidnightOriginal_BothPiecesOnSameDay_LeavesTheOtherDayBelowItsFloor_IsInvalid(StrictnessRule rule)
    {
        // Both pieces land on day 1, so day 2 keeps only its other committed hour — below its 2h
        // floor. The same answer whichever strictness: a day's floor is a day's floor.
        var (blockId, day1, _) = CrossMidnightBlockUnder(rule, floorHours: 2);
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(10), day1.AddHours(11)),
            Second = At.Slot(day1.AddHours(11), day1.AddHours(12))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This would drop Jan 4's total below its 2h daily floor.");
    }

    [Fact]
    public async Task Validate_CrossMidnightOriginal_PiecesOnEitherDay_BothDaysMeetTheFloor_IsValid()
    {
        var (blockId, day1, day2) = CrossMidnightBlockUnder(StrictnessRule.DailyFlexibility, floorHours: 1);
        var command = new SplitPlannedBlockCommand
        {
            OriginalBlockId = blockId,
            First = At.Slot(day1.AddHours(23), day2), // 60 min, entirely Day1 — meets its 1h floor
            Second = At.Slot(day2, day2.AddHours(1)), // 60 min, entirely Day2 — meets its 1h floor
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    private static NenContract SealedPeriodBudgetContract(Guid id, double contractFloorHours, DateOnly day1, DateOnly day2) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            rules: TestRules.Of(StrictnessRule.PeriodTimeBudget),
            calendar: TestCalendars.For(day1, day2, new DailyFloorRule.None()),
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [day1] = contractFloorHours }, // the target is what was signed
            sealedAt: TestAt.Utc(day1.ToDateTime(TimeOnly.MinValue)), timeZone: TestAt.UtcZone);

    [Theory]
    [InlineData(10, false)] // the contract's only block is 3h — under a 10h target whatever the split
    [InlineData(3, true)]
    public async Task Validate_PeriodTimeBudgetWithAWholePeriodTarget_ChecksTheTotal(double targetHours, bool valid)
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var day1 = DateOnly.FromDateTime(OriginalStart);
        SetUpDailyFlexBlock(blockId, contractId); // reuse for block wiring only
        var block = await _blocks.GetByIdAsync(blockId);
        _blocks.GetForContractAsync(contractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block!]);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedPeriodBudgetContract(contractId, targetHours, day1, day1.AddDays(30)));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Validate_PermissionRefused_PlacementIsNotAsked()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.Locked, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This block is locked by a Lockdown contract.");
        await _blocks.DidNotReceive().GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_AFreeBonusBlock_CannotBeSplit()
    {
        var blockId = Guid.NewGuid();
        var contract = SealedContract(StrictnessRule.DailyFlexibility, Guid.NewGuid());
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), TestAt.Slot(OriginalStart, OriginalEnd),
                membership: contract.MembershipForPlacedBlock()));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Only a Focus block that a Nen Contract binds can be split.");
    }
    [Fact]
    public async Task Validate_FirstBlockOutsideWorkHours_IsInvalid()
    {
        // Work hours 15:00-18:00 — First (14:00-16:00, from ValidCommand's 2h split point)
        // starts an hour before the window opens.
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
        var contract = TestContracts.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.None(), workHours: new WorkHours(new TimeOnly(15, 0), new TimeOnly(18, 0))),
                floorRule: new DailyFloorRule.None(),
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
        var contract = TestContracts.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.None(), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(16, 0))),
                floorRule: new DailyFloorRule.None(),
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: TestAt.Utc(Now), timeZone: TestAt.UtcZone);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BothBlocksWithinWorkHours_IsValid()
    {
        var blockId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contractId)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(TestContracts.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.None(), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0))),
                floorRule: new DailyFloorRule.None(),
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
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(),TestAt.Slot(day1.AddHours(22), day2.AddHours(2)), membership: TestContracts.Committed(contractId))); // 4h
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
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(running);

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