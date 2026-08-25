using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
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
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();

    public CreatePlannedBlockValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        // Default: echoes back whatever ContractId was scoped to, no redemption entries —
        // matches the real handler's behavior when nothing is eligible to cover.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedemptionAttributionResult(((ComputeRedemptionAttributionQuery)ci[0]).ContractId, []));
    }

    private CreatePlannedBlockValidator CreateValidator() => new(_presets, _contracts, _clock, _computeRedemptionAttribution);

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
    public async Task Validate_FocusBlock_ResolvesWorkHoursContractThroughAttributionQuery()
    {
        // Even with no explicit ContractId on the command, a Focus block's Work Hours
        // check must be resolved against whatever contract ComputeRedemptionAttribution
        // auto-discovers — not skipped just because command.ContractId is null.
        var presetId = Guid.NewGuid();
        var discoveredContractId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Now));
        var workHours = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        _contracts.GetByIdAsync(discoveredContractId, Arg.Any<CancellationToken>())
            .Returns(NenContract.Seal(discoveredContractId, "Contract", new NenContractGoal("Goal"),
                DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
                new Dictionary<DateOnly, double>(), Now, workHoursPolicy: workHours));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(discoveredContractId, [new(Guid.NewGuid(), 30)]));

        var command = ValidCommand(presetId) with { Start = Now.Date.AddHours(20), End = Now.Date.AddHours(21) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
