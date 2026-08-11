using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckMakeUpForEntry;
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

    public UpdatePlannedBlockValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _checkMakeUpForEntry.HandleAsync(Arg.Any<CheckMakeUpForEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpForEntryCheckResult.Valid);
    }

    private UpdatePlannedBlockValidator CreateValidator() => new(_blocks, _presets, _contracts, _clock, _checkMakeUpForEntry);

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
}
