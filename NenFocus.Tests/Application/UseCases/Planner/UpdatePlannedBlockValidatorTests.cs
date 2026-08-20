using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckMakeUpForEntry;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
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
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly IQueryHandler<CheckMakeUpForEntryQuery, MakeUpForEntryCheckResult> _checkMakeUpForEntry =
        Substitute.For<IQueryHandler<CheckMakeUpForEntryQuery, MakeUpForEntryCheckResult>>();
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _computePlannedFocusHours =
        Substitute.For<IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();

    public UpdatePlannedBlockValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _checkMakeUpForEntry.HandleAsync(Arg.Any<CheckMakeUpForEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpForEntryCheckResult.Valid);
        _computePlannedFocusHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, double>());
    }

    private UpdatePlannedBlockValidator CreateValidator() =>
        new(_blocks, _presets, _contracts, _clock, _checkMakeUpForEntry, _computePlannedFocusHours);

    private static UpdatePlannedBlockCommand ValidCommand(Guid blockId, Guid presetId) => new()
    {
        BlockId = blockId,
        PresetId = presetId,
        Start = Now.AddHours(1),
        End = Now.AddHours(2)
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));

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
        var endedBlock = FocusBlock.Create(blockId, presetId, Now.AddHours(-3), Now.AddHours(-2));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(endedBlock);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(-3), End = Now.AddHours(-1) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_StartInPast_ButBlockAlreadyStarted_SkipsFutureCheck()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        // Existing block's own Start is already in the past (it's live/locked).
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddMinutes(-30), Now.AddHours(1)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        // The UI sends back the original (now-past) start time unchanged.
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddMinutes(-30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_StartInPast_BlockNotYetStarted_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddMinutes(-10) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EndBeforeStart_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(blockId, presetId) with { End = Now.AddMinutes(30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BackgroundBlockWithMakeUpForEntries_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, Now));
        var command = ValidCommand(blockId, presetId) with { MakeUpForEntries = [new(Guid.NewGuid(), 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FocusBlockWithMakeUpForEntries_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(blockId, presetId) with { MakeUpForEntries = [new(Guid.NewGuid(), 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MakeUpForEntryFailsCheck_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _checkMakeUpForEntry.HandleAsync(Arg.Any<CheckMakeUpForEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpForEntryCheckResult.WrongDay);
        var command = ValidCommand(blockId, presetId) with { MakeUpForEntries = [new(Guid.NewGuid(), 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockHasNoContractId_SkipsWorkHoursCheck()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractHasNoWorkHoursPolicy_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.PeriodTimeBudget, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockOutsideWorkHours_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var workHours = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.PeriodTimeBudget, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now, workHoursPolicy: workHours));
        var command = ValidCommand(blockId, presetId) with { Start = Now.Date.AddHours(20), End = Now.Date.AddHours(21) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockInsideWorkHours_IsValid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var workHours = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.PeriodTimeBudget, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now, workHoursPolicy: workHours));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId)); // Now+1h..Now+2h = 13:00-14:00

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MakeUpForEntry_DispatchesCheckWithThisBlockAsExcludeBlockId()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var missedBlockId = Guid.NewGuid();
        var command = ValidCommand(blockId, presetId) with { MakeUpForEntries = [new(missedBlockId, 30)] };

        await CreateValidator().ValidateAsync(command);

        await _checkMakeUpForEntry.Received(1).HandleAsync(
            Arg.Is<CheckMakeUpForEntryQuery>(q => q.MissedBlockId == missedBlockId && q.ExcludeBlockId == blockId),
            Arg.Any<CancellationToken>());
    }

    // ── Sealed-contract strictness guards ──────────────────────────────────────

    private static NenContract SealedContract(StrictnessRule rule, Guid id) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"),
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
            rule, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), sealedAt: Now); // Active on seal

    [Fact]
    public async Task Validate_LockedContract_AnyUpdateIsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(3), End = Now.AddHours(4) };

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
            .Returns(FocusBlock.Create(blockId, presetId, AtLocal(14), AtLocal(16), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = ValidCommand(blockId, presetId) with { Start = AtLocal(22), End = AtLocal(24) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.DailyFlexibility, contractId));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddDays(1).AddHours(1), End = Now.AddDays(1).AddHours(2) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddDays(3), End = Now.AddDays(3).AddHours(1) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddDays(10), End = Now.AddDays(10).AddHours(1) };

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
            .Returns(FocusBlock.Create(blockId, originalPresetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(newPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(newPresetId, "Other", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId));
        // Same Start/End as the existing block — only the preset differs.
        var command = ValidCommand(blockId, newPresetId) with { Start = Now.AddHours(1), End = Now.AddHours(2) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var contract = SealedContract(StrictnessRule.Locked, contractId);
        contract.Complete(Now.AddDays(8));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(contract);

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, presetId));

        result.IsValid.Should().BeTrue();
    }

    // ── Daily Flexibility floor-protection ──────────────────────────────────────

    private static NenContract SealedContractWithFloor(StrictnessRule rule, Guid id, double floorHours) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"),
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
            rule, new DailyFloorPolicy(new DailyFloor(floorHours)), null, false,
            // Satisfies NenContract.Seal's own floor invariant for every day in range at
            // construction time — unrelated to the validator's later _computePlannedFocusHours
            // query, which is mocked separately per test to represent the state AFTER the edit.
            Enumerable.Range(0, 8).ToDictionary(i => DateOnly.FromDateTime(Now).AddDays(i), _ => floorHours),
            sealedAt: Now);

    [Fact]
    public async Task Validate_DailyFlexibility_ShrinkBelowFloor_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId)); // 2h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.DailyFlexibility, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(1), End = Now.AddHours(2) }; // shrunk to 1h
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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(4), contractId: contractId)); // 3h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.DailyFlexibility, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(1), End = Now.AddHours(3) }; // shrunk to 2h, exactly the floor
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
            .Returns(BackgroundBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.DailyFlexibility, contractId, floorHours: 2));
        // Shrunk well below what would be the floor for a Focus block — irrelevant here,
        // Background blocks never count toward the floor.
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(1), End = Now.AddHours(1).AddMinutes(10) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(4), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with
        {
            Start = Now.AddDays(3), End = Now.AddDays(3).AddMinutes(10) // shrunk to 10 minutes, elsewhere in the period
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    // ── Period Time Budget floor-protection (ContractFloor) ─────────────────────

    private static NenContract SealedContractWithContractFloor(Guid id, double contractFloorHours) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"),
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
            StrictnessRule.PeriodTimeBudget, DailyFloorPolicy.None, null, false,
            new Dictionary<DateOnly, double>(), sealedAt: Now,
            contractFloor: new ContractFloor(contractFloorHours));

    [Fact]
    public async Task Validate_PeriodTimeBudget_ShrinkBelowContractFloor_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId)); // 2h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithContractFloor(contractId, contractFloorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(1), End = Now.AddHours(2) }; // shrunk to 1h
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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId)); // 2h originally
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithContractFloor(contractId, contractFloorHours: 2));
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(1), End = Now.AddHours(2) }; // shrunk to 1h here...
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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContract(StrictnessRule.PeriodTimeBudget, contractId)); // floor = 0 every day, no ContractFloor
        // Shrunk to almost nothing — the per-day guard still runs, but a 0h floor is
        // always trivially satisfied regardless of total.
        var command = ValidCommand(blockId, presetId) with { Start = Now.AddHours(1), End = Now.AddHours(1).AddMinutes(5) };

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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        // Moved to a different day entirely — the origin day now has nothing scheduled.
        var command = ValidCommand(blockId, presetId) with
        {
            Start = Now.AddDays(3).AddHours(1),
            End = Now.AddDays(3).AddHours(3)
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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(2), contractId: contractId)); // 1h
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        // Origin day still meets its floor via other blocks; destination day only gets
        // this 1h block, which alone doesn't reach its own 2h floor.
        var command = ValidCommand(blockId, presetId) with
        {
            Start = Now.AddDays(3).AddHours(1),
            End = Now.AddDays(3).AddHours(2)
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
            .Returns(FocusBlock.Create(blockId, presetId, Now.AddHours(1), Now.AddHours(3), contractId: contractId));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(SealedContractWithFloor(StrictnessRule.PeriodTimeBudget, contractId, floorHours: 2));
        var command = ValidCommand(blockId, presetId) with
        {
            Start = Now.AddDays(3).AddHours(1),
            End = Now.AddDays(3).AddHours(3)
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
