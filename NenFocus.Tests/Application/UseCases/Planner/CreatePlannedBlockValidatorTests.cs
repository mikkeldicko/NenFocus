using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckRedemptionEntry;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class CreatePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly IQueryHandler<CheckRedemptionEntryQuery, RedemptionEntryCheckResult> _checkRedemptionEntry =
        Substitute.For<IQueryHandler<CheckRedemptionEntryQuery, RedemptionEntryCheckResult>>();

    public CreatePlannedBlockValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _checkRedemptionEntry.HandleAsync(Arg.Any<CheckRedemptionEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(RedemptionEntryCheckResult.Valid);
    }

    private CreatePlannedBlockValidator CreateValidator() => new(_presets, _contracts, _blocks, _clock, _checkRedemptionEntry);

    private static CreatePlannedBlockCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        BlockPresetType = BlockPresetType.Focus,
        Start = Now.AddHours(1),
        End = Now.AddHours(2)
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyPresetId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PresetTypeMismatch_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_StartInPast_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(presetId) with { Start = Now.AddMinutes(-10) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EndBeforeStart_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(presetId) with { End = Now.AddMinutes(30) - TimeSpan.FromHours(2) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ShorterThanOneMinute_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(presetId) with { End = ValidCommand(presetId).Start.AddSeconds(30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ExactlyOneMinute_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(presetId) with { End = ValidCommand(presetId).Start.AddMinutes(1) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlockWithRedemptionEntries_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, Now));
        var command = ValidCommand(presetId) with
        {
            BlockPresetType = BlockPresetType.Background,
            RedemptionEntries = [new(Guid.NewGuid(), 30)]
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FocusBlockWithRedemptionEntries_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var command = ValidCommand(presetId) with { RedemptionEntries = [new(Guid.NewGuid(), 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RedemptionEntryFailsCheck_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _checkRedemptionEntry.HandleAsync(Arg.Any<CheckRedemptionEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(RedemptionEntryCheckResult.ExceedsRemainingDeficit);
        var command = ValidCommand(presetId) with { RedemptionEntries = [new(Guid.NewGuid(), 999)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NoContractId_SkipsWorkHoursCheck()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractHasNoWorkHoursPolicy_IsValid()
    {
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now));
        var command = ValidCommand(presetId) with { ContractId = contractId };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockOutsideWorkHours_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var workHours = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now, workHoursPolicy: workHours));
        // ValidCommand runs Now+1h to Now+2h; Now is 12:00, so 13:00-14:00 — inside 9-17, flip it outside.
        var command = ValidCommand(presetId) with
        {
            ContractId = contractId,
            Start = Now.Date.AddHours(20),
            End = Now.Date.AddHours(21)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockInsideWorkHours_IsValid()
    {
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var workHours = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(contractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now, workHoursPolicy: workHours));
        var command = ValidCommand(presetId) with { ContractId = contractId }; // Now+1h..Now+2h = 13:00-14:00

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RedemptionEntry_DispatchesCheckWithNullExcludeBlockId()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var missedBlockId = Guid.NewGuid();
        var command = ValidCommand(presetId) with { RedemptionEntries = [new(missedBlockId, 30)] };

        await CreateValidator().ValidateAsync(command);

        await _checkRedemptionEntry.Received(1).HandleAsync(
            Arg.Is<CheckRedemptionEntryQuery>(q => q.MissedBlockId == missedBlockId && q.ExcludeBlockId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_RedemptionEntry_DifferentContractThanMissedBlock_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        var missedBlockContractId = Guid.NewGuid();
        var redemptionContractId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var missedBlockId = Guid.NewGuid();
        _blocks.GetByIdAsync(missedBlockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(missedBlockId, presetId, Now.AddHours(-3), Now.AddHours(-1), contractId: missedBlockContractId));
        var command = ValidCommand(presetId) with { ContractId = redemptionContractId, RedemptionEntries = [new(missedBlockId, 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_RedemptionEntry_SameContractAsMissedBlock_IsValid()
    {
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var missedBlockId = Guid.NewGuid();
        _blocks.GetByIdAsync(missedBlockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(missedBlockId, presetId, Now.AddHours(-3), Now.AddHours(-1), contractId: contractId));
        var command = ValidCommand(presetId) with { ContractId = contractId, RedemptionEntries = [new(missedBlockId, 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RedemptionEntry_MissedBlockNotFound_SkipsContractCheck()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var missedBlockId = Guid.NewGuid();
        _blocks.GetByIdAsync(missedBlockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);
        var command = ValidCommand(presetId) with { RedemptionEntries = [new(missedBlockId, 30)] };

        var result = await CreateValidator().ValidateAsync(command);

        // CheckRedemptionEntryQuery (mocked Valid) is the rule that owns "not found" reporting.
        result.IsValid.Should().BeTrue();
    }
}
