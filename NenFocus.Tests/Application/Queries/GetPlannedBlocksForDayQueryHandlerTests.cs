using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetPlannedBlocksForDayQueryHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 1, 2);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetPlannedBlocksForDayQueryHandlerTests()
    {
        _clock.SetNow(new DateTime(2026, 1, 2, 10, 30, 0, DateTimeKind.Utc)); // mid-window by default
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);
    }

    private GetPlannedBlocksForDayQueryHandler CreateHandler() => new(_blocks, _presets, _contracts, _clock,
        new BlockChangeChecker());

    [Fact]
    public async Task HandleAsync_FocusBlock_MapsPresetNameAndBlockPresetType()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockPreset>)[BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Allowlist, BlockPresetType.Focus, At.Utc(Start))]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Deep Work");
        dto.BlockPresetType.Should().Be(BlockPresetType.Focus);
        dto.WebsiteFilterType.Should().Be(WebsiteFilterType.Allowlist);
        dto.Shown.Should().Be(At.Slot(Start, End));
        dto.IsFinal.Should().BeFalse();
        dto.ContractBadge.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithRedemptionEntries_ContractBadgeIsRedemption()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), presetId,At.Slot(Start, End),
            title: null, membership: TestContracts.Redemption(Guid.NewGuid()), receipt: new RedemptionReceipt([new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(30))]));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.ContractBadge.Should().Be(PlannedBlockContractBadge.Redemption);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockEndedEarly_DisplayEndIsSessionEndedAt()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Shown.Should().Be(At.Slot(Start, Start.AddMinutes(20)));
        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_FocusBlockStartedLate_DisplayStartIsRitualCompletedAt()
    {
        var ritualCompletedAt = Start.AddMinutes(FocusBlock.GracePeriodMinutes + 1);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Shown.Should().Be(At.Slot(ritualCompletedAt, End));
        block.Scheduled.Should().Be(At.Slot(Start, End)); // underlying scheduled start untouched
    }

    [Fact]
    public async Task HandleAsync_FocusBlockRitualCompletedWithinGracePeriod_DisplayStartIsScheduledStart()
    {
        var ritualCompletedAt = Start.AddMinutes(FocusBlock.GracePeriodMinutes);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        // Full credit within grace means full-width display too — no trim.
        result.Should().ContainSingle().Subject.Shown.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_FocusBlockStartedLateAndEndedEarly_DisplayShortenedFromBothEnds()
    {
        var ritualCompletedAt = Start.AddMinutes(10);
        var sessionEndedAt = Start.AddMinutes(30);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        block.MarkSessionEnded(At.Utc(sessionEndedAt));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Shown.Should().Be(At.Slot(ritualCompletedAt, sessionEndedAt));
        // Classify gives EndedEarly priority, but StartedLate is independent — the block
        // was genuinely both, and the badge needs to be able to say so.
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.StartedLate.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockEndedEarlyButRitualWasOnTime_StartedLateIsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.StartedLate.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AsksForTheUsersLocalDay_NotTheUtcDay()
    {
        // Gap 97: the day is the user's calendar day, turned into real time by the time zone.
        // At UTC+2 it starts at 22:00 UTC the evening before. (Cancelled blocks are left out
        // by the repository — PlannedBlockRepositoryTests.)
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var dayStartUtc = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-2);

        await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        await _blocks.Received(1).GetForRangeAsync(At.Slot(dayStartUtc, dayStartUtc.AddDays(1)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithoutSessionEnded_DisplayEndIsScheduledEnd()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Shown.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_BlockAtItsStart_AllowsNothing()
    {
        _clock.SetNow(Start);
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Allowances.Should().Be(new BlockChangeAllowances(false, false, false, false));
    }

    [Fact]
    public async Task HandleAsync_BlockAlreadyEnded_IsFinal()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWhoseSessionIsStillRunning_IsNotFinal()
    {
        // It has an outcome already (the Ritual was on time) but could still end early — the
        // block's own answer, never the Planner's "has an outcome" (Gap 89 Slice B, A3).
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().NotBeNull();
        dto.IsFinal.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockMissed_OutcomeIsMissed()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.Missed);
        dto.Shown.Should().Be(At.Slot(Start, End)); // full original span still displayed — only the color changes
    }

    [Fact]
    public async Task HandleAsync_FocusBlockRitualCompletedButWindowClosed_OutcomeIsCompletedOnTime()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Outcome.Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockRitualCompletedLate_OutcomeIsStartedLate()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start.AddMinutes(FocusBlock.GracePeriodMinutes + 1)));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.StartedLate);
        dto.StartedLate.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockStillPending_WindowNotYetClosed_OutcomeIsNull()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlockWindowClosed_OutcomeIsNull()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_MapsBlockPresetType()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.BlockPresetType.Should().Be(BlockPresetType.Background);
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_UsesUnknownNameAndBlocklistDefault()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Unknown");
        dto.WebsiteFilterType.Should().Be(WebsiteFilterType.Blocklist);
    }

    // ── Allowances: BlockChangeChecker's answer, handed the block's own contract (Gap 93) ──

    private NenContract SetUpContractInEffect(Guid contractId, StrictnessRule strictness)
    {
        var rules = TestRules.Of(strictness);
        var contract = NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), rules.FloorRule),
            new Dictionary<DateOnly, double>(), At.Utc(Start.Date), At.UtcZone);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        return contract;
    }

    private async Task<BlockChangeAllowances> AllowancesBeforeItStartsAsync(PlannedBlock block)
    {
        _clock.SetNow(Start.AddHours(-1));
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        return result.Should().ContainSingle().Subject.Allowances;
    }

    [Fact]
    public async Task HandleAsync_APlainFutureBlock_MayBeRescheduledReSetAndDeleted()
    {
        var allowances = await AllowancesBeforeItStartsAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End)));

        allowances.Should().Be(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: true, CanSplit: false, CanDelete: true));
    }

    [Theory]
    [InlineData(StrictnessRule.Locked, false, false)]
    [InlineData(StrictnessRule.DailyFlexibility, true, true)]
    [InlineData(StrictnessRule.PeriodTimeBudget, true, true)]
    public async Task HandleAsync_ACommittedBlock_AllowancesFollowItsContractsStrictness(
        StrictnessRule strictness, bool canReschedule, bool canSplit)
    {
        var contractId = Guid.NewGuid();
        SetUpContractInEffect(contractId, strictness);

        var allowances = await AllowancesBeforeItStartsAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(contractId)));

        allowances.Should().Be(new BlockChangeAllowances(canReschedule, CanChangePreset: false, canSplit, CanDelete: false)
        {
            SplitPiecesMayChangeDay = strictness == StrictnessRule.PeriodTimeBudget
        });
    }

    [Fact]
    public async Task HandleAsync_AFreeBonusBlock_HasAPlainBlocksAllowances_EvenUnderLockdown()
    {
        var contract = SetUpContractInEffect(Guid.NewGuid(), StrictnessRule.Locked);

        var allowances = await AllowancesBeforeItStartsAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: contract.MembershipForPlacedBlock()));

        allowances.Should().Be(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: true, CanSplit: false, CanDelete: true));
    }

    [Fact]
    public async Task HandleAsync_ABlockWhoseContractIsFinishedOrGone_AllowsNothing()
    {
        // _contracts returns null for an unknown id — a member without its contract is history.
        var allowances = await AllowancesBeforeItStartsAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(Guid.NewGuid())));

        allowances.Should().Be(new BlockChangeAllowances(false, false, false, false));
    }
    // ── Contract badge: the block's own stored role (Gap 97) ─────────────────

    private NenContract SetUpStudyMonth(Guid contractId, bool broken = false)
    {
        var contract = TestContracts.Seal(
            id: contractId,
            name: "Study Month",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Day.AddDays(-1), Day.AddDays(5), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(-1)), timeZone: At.UtcZone);
        if (broken) contract.Break(At.Utc(Start), At.UtcZone, sessionRunning: false);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        return contract;
    }

    private async Task<PlannedBlockDto> SingleDtoAsync(PlannedBlock block)
    {
        _blocks.GetForRangeAsync(At.UtcZone.SlotCovering(Day), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        return result.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task HandleAsync_BonusFocusBlock_ContractBadgeIsBonusWithContractName()
    {
        var contractId = Guid.NewGuid();
        SetUpStudyMonth(contractId);

        var dto = await SingleDtoAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Bonus(contractId)));

        dto.ContractBadge.Should().Be(PlannedBlockContractBadge.Bonus);
        dto.BonusContractName.Should().Be("Study Month");
    }

    [Fact]
    public async Task HandleAsync_BonusBackgroundBlock_HasNoBonusBadge()
    {
        // Bonus hours only ever come from Focus blocks, so a Background block that belongs to
        // a contract as Bonus must not be badged as one.
        var contractId = Guid.NewGuid();
        SetUpStudyMonth(contractId);

        var dto = await SingleDtoAsync(
            BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Bonus(contractId)));

        dto.ContractBadge.Should().BeNull();
        dto.BonusContractName.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_PlainBlock_ContractBadgeIsNull_EvenInsideAContractsDates()
    {
        // No membership means no badge — the dates are never consulted. (A block made before
        // the contract was sealed sits inside its dates without belonging to it.)
        SetUpStudyMonth(Guid.NewGuid());

        var dto = await SingleDtoAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End)));

        dto.ContractBadge.Should().BeNull();
        dto.BonusContractName.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_CommittedFocusBlock_ContractBadgeIsCommitted()
    {
        var contractId = Guid.NewGuid();
        SetUpStudyMonth(contractId);

        var dto = await SingleDtoAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(contractId)));

        dto.ContractBadge.Should().Be(PlannedBlockContractBadge.Committed);
    }

    [Fact]
    public async Task HandleAsync_CommittedBackgroundBlock_HasNoBadge()
    {
        // The floor is Focus-only, so a drafted Background block has nothing to tell apart.
        var contractId = Guid.NewGuid();
        SetUpStudyMonth(contractId);

        var dto = await SingleDtoAsync(
            BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Committed(contractId)));

        dto.ContractBadge.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_RedemptionBlockThatCoversNothingAnyMore_ContractBadgeIsStillRedemption()
    {
        // The role is the answer, not the entries: coverage can drop to zero on an edit.
        var contractId = Guid.NewGuid();
        SetUpStudyMonth(contractId);

        var dto = await SingleDtoAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Redemption(contractId)));

        dto.ContractBadge.Should().Be(PlannedBlockContractBadge.Redemption);
    }

    [Fact]
    public async Task HandleAsync_BonusBlockOfABrokenContract_ContractBadgeIsStillBonus()
    {
        // A Bonus block that ran keeps its membership when its contract is broken.
        var contractId = Guid.NewGuid();
        SetUpStudyMonth(contractId, broken: true);

        var dto = await SingleDtoAsync(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End), membership: TestContracts.Bonus(contractId)));

        dto.ContractBadge.Should().Be(PlannedBlockContractBadge.Bonus);
    }
}