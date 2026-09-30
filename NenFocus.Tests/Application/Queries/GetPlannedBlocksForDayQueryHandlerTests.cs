using FluentAssertions;
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
    private readonly IFocusBlockOutcomeClassifier _classifier = new FocusBlockOutcomeClassifier();

    public GetPlannedBlocksForDayQueryHandlerTests()
    {
        _clock.SetNow(new DateTime(2026, 1, 2, 10, 30, 0, DateTimeKind.Utc)); // mid-window by default
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);
    }

    private GetPlannedBlocksForDayQueryHandler CreateHandler() => new(_blocks, _presets, _contracts, _clock, _classifier);

    [Fact]
    public async Task HandleAsync_FocusBlock_MapsPresetNameAndBlockPresetType()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockPreset>)[BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Allowlist, BlockPresetType.Focus, At.Utc(Start))]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Deep Work");
        dto.BlockPresetType.Should().Be(BlockPresetType.Focus);
        dto.WebsiteFilterType.Should().Be(WebsiteFilterType.Allowlist);
        dto.Shown.Should().Be(At.Slot(Start, End));
        dto.IsCompleted.Should().BeFalse();
        dto.HasEnded.Should().BeFalse();
        dto.ContractBadge.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithRedemptionEntries_ContractBadgeIsRedemption()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End),
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 30)]);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.ContractBadge.Should().Be(PlannedBlockContractBadge.Redemption);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockEndedEarly_DisplayEndIsSessionEndedAt()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Shown.Should().Be(At.Slot(Start, Start.AddMinutes(20)));
        block.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_FocusBlockStartedLate_DisplayStartIsRitualCompletedAt()
    {
        var ritualCompletedAt = Start.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes + 1);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Shown.Should().Be(At.Slot(ritualCompletedAt, End));
        block.Scheduled.Should().Be(At.Slot(Start, End)); // underlying scheduled start untouched
    }

    [Fact]
    public async Task HandleAsync_FocusBlockRitualCompletedWithinGracePeriod_DisplayStartIsScheduledStart()
    {
        var ritualCompletedAt = Start.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(ritualCompletedAt));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
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
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Shown.Should().Be(At.Slot(ritualCompletedAt, sessionEndedAt));
        // Classify gives EndedEarly priority, but WasStartedLate is independent — the block
        // was genuinely both, and the badge needs to be able to say so.
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.WasStartedLate.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockEndedEarlyButRitualWasOnTime_WasStartedLateIsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.WasStartedLate.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CancelledBlock_ExcludedFromResults()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkCancelled(At.Utc(Start));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithoutSessionEnded_DisplayEndIsScheduledEnd()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Shown.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_BlockAtItsStart_HasStartedIsTrue()
    {
        _clock.SetNow(Start);
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.HasStarted.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BlockAlreadyEnded_HasEndedIsTrue()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.HasEnded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockMissed_OutcomeIsMissed()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
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
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Outcome.Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockRitualCompletedLate_OutcomeIsStartedLate()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes + 1)));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.StartedLate);
        dto.WasStartedLate.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_FocusBlockStillPending_WindowNotYetClosed_OutcomeIsNull()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlockWindowClosed_OutcomeIsNull()
    {
        _clock.SetNow(End.AddMinutes(1));
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_MapsBlockPresetType()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.BlockPresetType.Should().Be(BlockPresetType.Background);
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_UsesUnknownNameAndBlocklistDefault()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Unknown");
        dto.WebsiteFilterType.Should().Be(WebsiteFilterType.Blocklist);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithCompletedRitual_IsCompletedIsTrue()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BlockUnderActiveContract_IsUnderInEffectContractIsTrue()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), contractId: contractId);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(
            NenContract.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.Locked,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), new DailyFloorRule.Fixed(DailyFloor.None)),
                floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
                changeWindow: null,
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: At.Utc(Start.Date), timeZone: At.UtcZone));

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsUnderInEffectContract.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BlockWithNoContract_IsUnderInEffectContractIsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsUnderInEffectContract.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_BlockUnderCompletedContract_IsUnderInEffectContractIsFalse()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), contractId: contractId);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var contract = NenContract.Seal(
            id: contractId,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Start).AddDays(-10), DateOnly.FromDateTime(Start).AddDays(-3), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(-10)), timeZone: At.UtcZone);
        contract.Complete(At.Utc(Start.AddDays(-2)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsUnderInEffectContract.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_BlockUnderDailyFlexibilityContract_ContractStrictnessRuleIsDailyFlexibility()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), contractId: contractId);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(
            NenContract.Seal(
                id: contractId,
                name: "Contract",
                goal: new NenContractGoal("Goal"),
                strictnessRule: StrictnessRule.DailyFlexibility,
                calendar: TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), new DailyFloorRule.Fixed(DailyFloor.None)),
                floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
                changeWindow: null,
                allowCancellation: false,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
                sealedAt: At.Utc(Start.Date), timeZone: At.UtcZone));

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.ContractStrictnessRule.Should().Be(StrictnessRule.DailyFlexibility);
    }

    [Fact]
    public async Task HandleAsync_BlockWithNoContract_ContractStrictnessRuleIsNull()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.ContractStrictnessRule.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockWithinAnActiveContractRange_ContractBadgeIsBonusWithContractName()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)); // ContractId null
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Study Month",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Day.AddDays(-1), Day.AddDays(5), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(-1)), timeZone: At.UtcZone);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.ContractBadge.Should().Be(PlannedBlockContractBadge.Bonus);
        dto.BonusContractName.Should().Be("Study Month");
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBackgroundBlockWithinAContractRange_HasNoBonusBadge()
    {
        // Bonus hours only ever come from Focus blocks, so a Background block inside a
        // contract's range is not a Bonus block and must not be badged as one.
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)); // ContractId null
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Study Month",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Day.AddDays(-1), Day.AddDays(5), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(-1)), timeZone: At.UtcZone);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.ContractBadge.Should().BeNull();
        dto.BonusContractName.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockOutsideAnyContractRange_ContractBadgeIsNull()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)); // ContractId null
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Study Month",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Day.AddDays(10), Day.AddDays(20), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(10)), timeZone: At.UtcZone);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.ContractBadge.Should().BeNull();
        dto.BonusContractName.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_LinkedBlockWithinAContractRange_ContractBadgeIsNull()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), contractId: contractId);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var contract = NenContract.Seal(
            id: contractId,
            name: "Study Month",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Day.AddDays(-1), Day.AddDays(5), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(-1)), timeZone: At.UtcZone);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        // Already committed to its own contract — never also labeled Bonus.
        result.Should().ContainSingle().Subject.ContractBadge.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnlinkedBlockWithinABrokenContractRange_ContractBadgeIsBonus()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)); // ContractId null
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Study Month",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Day.AddDays(-1), Day.AddDays(5), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date.AddDays(-1)), timeZone: At.UtcZone);
        contract.Break(At.Utc(Start), At.UtcZone);
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        // Bonus status is regardless of the covering contract's current status.
        result.Should().ContainSingle().Subject.ContractBadge.Should().Be(PlannedBlockContractBadge.Bonus);
    }
}
