using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class UpdatePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();
    // The real permission answer (Gap 93) — a Domain service with no I/O. Its rules are tested on
    // NenContract, PlannedBlock and BlockChangeChecker; here only that the validator asks it,
    // hands it the right things, and passes its refusal on.
    private readonly IBlockChangeChecker _changes = new BlockChangeChecker();
    // Placement (past, contract hours, free for Focus) is GetBlockPlacementVerdictQuery's answer
    // since Gap 88 phase 8 — tested in GetBlockPlacementVerdictQueryHandlerTests. Allowed by default here.
    private readonly IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto> _placement =
        Substitute.For<IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto>>();

    public UpdatePlannedBlockValidatorTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _blocks.GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _placement.HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>())
            .Returns(new BlockPlacementVerdictDto { Outcome = PlacementOutcome.Allowed });
    }

    private UpdatePlannedBlockValidator CreateValidator() =>
        new(_blocks, _presets, _contracts, _clock, _changes, _placement);

    private static UpdatePlannedBlockCommand ValidCommand(Guid blockId, Guid presetId) => new()
    {
        BlockId = blockId,
        PresetId = presetId,
        Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockAlreadyEnded_IsInvalid()
    {
        // Mirrors DeletePlannedBlockValidator's identical guard — a block whose window
        // has already fully closed (e.g. Missed) is kept as a historical record and must
        // be immutable to Update too, not just Delete.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var endedBlock = FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(-3), Now.AddHours(-2)));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(endedBlock);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(-3), Now.AddHours(-1))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyPresetId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid(), Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PresetTypeDoesNotMatchBlockPresetType_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Now)));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockAlreadyStarted_EndOnlyEdit_IsInvalid()
    {
        // Gap 88 phase 0: a started block is locked — not even its end may change.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddMinutes(-30), Now.AddHours(1))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddMinutes(-30), Now.AddHours(2))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("already started"));
    }

    [Fact]
    public async Task Validate_BlockStartsExactlyNow_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, presetId,At.Slot(Now, Now.AddHours(1))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))};

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("already started"));
    }

    [Fact]
    public async Task Validate_FocusBlock_ExceedsMaxDuration_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId) with
{
    Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddHours(FocusBlock.MaxDurationHours).AddMinutes(1))
};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FocusBlock_AtExactlyMaxDuration_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId) with
{
    Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddHours(FocusBlock.MaxDurationHours))
};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlock_ExceedsFocusMaxDuration_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId) with
{
    Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddHours(FocusBlock.MaxDurationHours).AddDays(1))
};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PlacementRefused_IsInvalidWithTheQuerysMessage()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _placement.HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>())
            .Returns(new BlockPlacementVerdictDto { Outcome = PlacementOutcome.OutsideContractHours, Message = "Outside." });

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Outside.");
    }

    [Fact]
    public async Task Validate_AsksThePlacementQueryAsAnEditOfThisBlock()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var command = ValidCommand(blockId, presetId);

        await CreateValidator().ValidateAsync(command);

        await _placement.Received(1).HandleAsync(
            new GetBlockPlacementVerdictQuery(command.Slot, BlockPresetType.Focus, presetId, ExistingBlockId: blockId),
            Arg.Any<CancellationToken>());
    }

    // ── Sealed-contract strictness guards ──────────────────────────────────────

    private static NenContract SealedContract(StrictnessRule rule, Guid id) =>
        TestContracts.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone); // Active on seal

    [Fact]
    public async Task Validate_LockedContract_AMoveIsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.Locked, contractId));

        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(3), Now.AddHours(4)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This block is locked by a Lockdown contract.");
    }

    [Fact]
    public async Task Validate_LockedContract_SavingWithNothingChanged_IsValid()
    {
        // Only what changes is asked about (Gap 93) — an untouched form is not a move.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.Locked, contractId));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DailyFlexibilityContract_SameDayMoveIsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(3), Now.AddHours(4))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DailyFlexibilityContract_MoveToExactMidnightEnd_SameDayIsValid()
    {
        // A block moved to e.g. 22:00-24:00 has no actual presence on the next calendar
        // day — its End lands exactly on the day boundary, but it never crosses it.
        // localDay is 2 days out purely so every wall-clock hour on it converts to a UTC
        // instant safely after Now regardless of the test machine's timezone offset.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var localDay = Now.ToLocalTime().Date.AddDays(2);
        DateTime AtLocal(double hours) => DateTime.SpecifyKind(localDay.AddHours(hours), DateTimeKind.Local).ToUniversalTime();

        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(AtLocal(14), AtLocal(16)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(AtLocal(22), AtLocal(24))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DailyFlexibilityContract_CrossDayMoveIsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddDays(1).AddHours(1), Now.AddDays(1).AddHours(2))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetContract_WithinRangeMoveIsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddDays(3), Now.AddDays(3).AddHours(1))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetContract_OutsideContractRangeIsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddDays(10), Now.AddDays(10).AddHours(1))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PlainBlockMovedIntoAContractInEffect_IsInvalid_ADragNeverJoins()
    {
        // Gap 106, decision 18: the validator hands the checker every contract in effect.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SealedContract(StrictnessRule.PeriodTimeBudget, Guid.NewGuid()); // today + 7 days
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, At.Slot(Now.AddDays(10), Now.AddDays(10).AddHours(1))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId)); // into the contract

        result.Errors.Should().ContainSingle(e => e.ErrorMessage ==
            BlockChangeVerdictDto.From(ChangeVerdict.Refused(ChangeOutcome.WouldJoinContract)).Message);
    }

    [Fact]
    public async Task Validate_SealedContract_PresetChangeIsInvalidEvenWithoutTimeChange()
    {
        var blockId = Guid.NewGuid();
        var originalPresetId = Guid.NewGuid();
        var newPresetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, originalPresetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(newPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(newPresetId, "Other", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        // Same Start/End as the existing block — only the preset differs.
        var command = ValidCommand(blockId, newPresetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotInEffect_ItsBlocksAreHistory()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(contractId)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var contract = SealedContract(StrictnessRule.Locked, contractId);
        contract.Complete(At.Utc(Now.AddDays(8)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(contract);

        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(3), Now.AddHours(4)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("finished Nen Contract"));
    }

    // ── Floor protection, and the order of the two questions ───────────────────

    private static NenContract SealedContractWithFloor(StrictnessRule rule, Guid id, double floorHours) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            rules: TestRules.Of(rule, new DailyFloor(floorHours)),
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), new DailyFloorRule.Fixed(new DailyFloor(floorHours))),
            plannedFocusHoursByDay: Enumerable.Range(0, 8).ToDictionary(i => DateOnly.FromDateTime(Now).AddDays(i), _ => floorHours),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone);

    /// <summary>One committed two-hour Focus block on each of the contract's eight days, the edited one on day 0.</summary>
    private (FocusBlock Edited, Guid PresetId) ContractWithTwoHoursEveryDay(StrictnessRule rule)
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var blocks = Enumerable.Range(0, 8)
            .Select(day => FocusBlock.Create(Guid.NewGuid(), presetId,
                At.Slot(Now.AddDays(day).AddHours(1), Now.AddDays(day).AddHours(3)), membership: TestContracts.Committed(contractId)))
            .ToList();
        _blocks.GetByIdAsync(blocks[0].Id, Arg.Any<CancellationToken>()).Returns(blocks[0]);
        _blocks.GetForContractAsync(contractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)blocks);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(rule, contractId, floorHours: 2));
        return (blocks[0], presetId);
    }

    [Fact]
    public async Task Validate_ShrinkBelowTheDaysFloor_IsInvalidWithTheDayAndTheFloor()
    {
        var (block, presetId) = ContractWithTwoHoursEveryDay(StrictnessRule.DailyFlexibility);
        var command = ValidCommand(block.Id, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(2)) }; // shrunk to 1h

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This would drop Jan 1's total below its 2h daily floor.");
    }

    [Fact]
    public async Task Validate_MoveWithinTheDayKeepingItsLength_IsValid()
    {
        var (block, presetId) = ContractWithTwoHoursEveryDay(StrictnessRule.DailyFlexibility);
        var command = ValidCommand(block.Id, presetId) with { Slot = At.Slot(Now.AddHours(4), Now.AddHours(6)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MoveToAnotherDay_LeavingItsOwnDayShort_IsInvalid()
    {
        // The day left behind is checked although the block no longer touches it.
        var (block, presetId) = ContractWithTwoHoursEveryDay(StrictnessRule.PeriodTimeBudget);
        var command = ValidCommand(block.Id, presetId) with { Slot = At.Slot(Now.AddDays(2).AddHours(5), Now.AddDays(2).AddHours(7)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This would drop Jan 1's total below its 2h daily floor.");
    }

    [Fact]
    public async Task Validate_PermissionRefused_PlacementIsNotAsked()
    {
        // A running block answers "it has started", not placement's "that time is in the past".
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, At.Slot(Now.AddMinutes(-30), Now.AddHours(1))));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _placement.HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>())
            .Returns(new BlockPlacementVerdictDto { Outcome = PlacementOutcome.InThePast, Message = "In the past." });

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "This block has already started — it cannot be changed.");
        await _placement.DidNotReceive().HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_AFreeBonusBlock_MayChangeItsPreset_EvenUnderLockdown()
    {
        var blockId = Guid.NewGuid();
        var originalPresetId = Guid.NewGuid();
        var newPresetId = Guid.NewGuid();
        var contract = SealedContract(StrictnessRule.Locked, Guid.NewGuid());
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, originalPresetId, At.Slot(Now.AddHours(1), Now.AddHours(2)),
                membership: contract.MembershipForPlacedBlock()));
        _presets.GetByIdAsync(newPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(newPresetId, "Other", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, newPresetId));

        result.IsValid.Should().BeTrue();
    }
}
