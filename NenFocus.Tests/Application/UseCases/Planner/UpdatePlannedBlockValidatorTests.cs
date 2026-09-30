using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
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
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _computePlannedFocusHours =
        Substitute.For<IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    // Placement (past, contract hours, free for Focus) is GetBlockPlacementVerdictQuery's answer
    // since Gap 88 phase 8 — tested in GetBlockPlacementVerdictQueryHandlerTests. Allowed by default here.
    private readonly IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto> _placement =
        Substitute.For<IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto>>();

    public UpdatePlannedBlockValidatorTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>());
        _placement.HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>())
            .Returns(new BlockPlacementVerdictDto { Outcome = PlacementOutcome.Allowed });
    }

    private UpdatePlannedBlockValidator CreateValidator() =>
        new(_blocks, _presets, _contracts, _clock, _computePlannedFocusHours, _placement);

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
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone); // Active on seal

    [Fact]
    public async Task Validate_LockedContract_AnyUpdateIsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.Locked, contractId));

        // Same preset, same Start/End — a no-op resubmission still fails; Locked rejects
        // every edit outright, not just ones that would actually change something.
        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFlexibilityContract_SameDayMoveIsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
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
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(AtLocal(14), AtLocal(16)), contractId: contractId));
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
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
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
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
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
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddDays(10), Now.AddDays(10).AddHours(1))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SealedContract_PresetChangeIsInvalidEvenWithoutTimeChange()
    {
        var blockId = Guid.NewGuid();
        var originalPresetId = Guid.NewGuid();
        var newPresetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, originalPresetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
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
    public async Task Validate_ContractNotInEffect_GuardDoesNotApply()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        var contract = SealedContract(StrictnessRule.Locked, contractId);
        contract.Complete(At.Utc(Now.AddDays(8)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(contract);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeTrue();
    }

    // ── Daily Flexibility floor-protection ──────────────────────────────────────

    // plannedFocusHoursByDay satisfies NenContract.Seal's own floor invariant for every day in
    // range at construction time — unrelated to the validator's later _computePlannedFocusHours
    // query, which is mocked separately per test to represent the state AFTER the edit.
    private static NenContract SealedContractWithFloor(StrictnessRule rule, Guid id, double floorHours) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), new DailyFloorRule.Fixed(new DailyFloor(floorHours))),
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(floorHours)),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: Enumerable.Range(0, 8).ToDictionary(i => DateOnly.FromDateTime(Now).AddDays(i), _ => floorHours),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone);

    [Fact]
    public async Task Validate_DailyFlexibility_ShrinkBelowFloor_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId)); // 2h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.DailyFlexibility, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))}; // shrunk to 1h
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(Now)] = 1 });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_DailyFlexibility_ShrinkButStillMeetsFloor_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(4)), contractId: contractId)); // 3h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.DailyFlexibility, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(3))}; // shrunk to 2h, exactly the floor
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(Now)] = 2 });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DailyFlexibility_BackgroundBlockShrink_SkipsFloorCheck()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.DailyFlexibility, contractId, floorHours: 2));
        // Shrunk well below what would be the floor for a Focus block — irrelevant here,
        // Background blocks never count toward the floor.
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddMinutes(10))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudget_ShrinkToNearNothingElsewhereInPeriod_IsInvalid()
    {
        // Superseded expectation: Period Time Budget's per-day floor guard now covers
        // ordinary (non-ContractFloor) contracts too, mirroring Daily Flexibility — moving
        // hours across days is still fully allowed, but the landing day still has to meet
        // its own floor once the move settles. Both the vacated origin day and this new,
        // nearly-empty destination day are checked; the destination day alone (10 minutes
        // against a 2h floor) already fails.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(4)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with
        {
            Slot = At.Slot(Now.AddDays(3), Now.AddDays(3).AddMinutes(10)) // shrunk to 10 minutes, elsewhere in the period
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    // ── Period Time Budget floor-protection (ContractFloor) ─────────────────────

    private static NenContract SealedContractWithContractFloor(Guid id, double contractFloorHours) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.PeriodTimeBudget,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone,
            contractFloor: new ContractFloor(contractFloorHours));

    [Fact]
    public async Task Validate_PeriodTimeBudget_ShrinkBelowContractFloor_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId)); // 2h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithContractFloor(contractId, contractFloorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))}; // shrunk to 1h
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(Now)] = 1 });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudget_ShrinkOneDayButOtherDaysCoverContractFloor_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId)); // 2h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithContractFloor(contractId, contractFloorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))}; // shrunk to 1h here...
        // ...but another day already has 1h scheduled, so the period total still meets the 2h floor.
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>
            {
                [DateOnly.FromDateTime(Now)] = 1,
                [DateOnly.FromDateTime(Now).AddDays(3)] = 1
            });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudget_NoContractFloorAndNoDailyFloor_GuardTriviallyPasses()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId)); // floor = 0 every day, no ContractFloor
        // Shrunk to almost nothing — the per-day guard still runs, but a 0h floor is
        // always trivially satisfied regardless of total.
        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddMinutes(5))};

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    // ── Period Time Budget, ordinary per-day floor (not ContractFloor) ──────────

    [Fact]
    public async Task Validate_PeriodTimeBudgetFixedFloor_MoveVacatesOriginDayBelowFloor_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        // Moved to a different day entirely — the origin day now has nothing scheduled.
        var command = ValidCommand(blockId, presetId) with
        {
            Slot = At.Slot(Now.AddDays(3).AddHours(1), Now.AddDays(3).AddHours(3))
        };
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>
            {
                [DateOnly.FromDateTime(Now)] = 0, // vacated origin day — below its 2h floor
                [DateOnly.FromDateTime(Now.AddDays(3))] = 2
            });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetFixedFloor_MoveLandsOnDayBelowItsFloor_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(2)), contractId: contractId)); // 1h
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        // Origin day still meets its floor via other blocks; destination day only gets
        // this 1h block, which alone doesn't reach its own 2h floor.
        var command = ValidCommand(blockId, presetId) with
        {
            Slot = At.Slot(Now.AddDays(3).AddHours(1), Now.AddDays(3).AddHours(2))
        };
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>
            {
                [DateOnly.FromDateTime(Now)] = 2,
                [DateOnly.FromDateTime(Now.AddDays(3))] = 1 // destination day, below its 2h floor
            });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PeriodTimeBudgetFixedFloor_MoveBothDaysStillMeetTheirFloors_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Now.AddHours(1), Now.AddHours(3)), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with
        {
            Slot = At.Slot(Now.AddDays(3).AddHours(1), Now.AddDays(3).AddHours(3))
        };
        _computePlannedFocusHours.HandleAsync(
                Arg.Is<ComputePlannedFocusHoursQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>
            {
                [DateOnly.FromDateTime(Now)] = 2, // origin still covered by another block
                [DateOnly.FromDateTime(Now.AddDays(3))] = 2 // destination fully covered by the move
            });

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }
}
