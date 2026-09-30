using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class SealNenContractValidatorTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IContractPresetRepository _contractPresets = Substitute.For<IContractPresetRepository>();
    private readonly IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> _checkOverlap =
        Substitute.For<IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?>>();

    public SealNenContractValidatorTests()
    {
        _checkOverlap.HandleAsync(Arg.Any<CheckNenContractDateRangeOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
    }

    private SealNenContractValidator CreateValidator() => new(_presets, _contractPresets, _checkOverlap);

    private static SealNenContractCommand ValidCommand() => new()
    {
        Name = "Focused Week",
        Goal = "Ship it",
        StartDate = StartDate,
        EndDate = EndDate,
        StrictnessRule = StrictnessRule.Locked,
        Blocks = []
    };

    [Fact]
    public async Task Validate_BlankName_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Name = "  " });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NoContractPresetId_IsValid()
    {
        // A Simple contract legitimately has no preset at all.
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { ContractPresetId = null });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractPresetIdDoesNotExist_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _contractPresets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((ContractPreset?)null);

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { ContractPresetId = presetId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractPresetIdExists_IsValid()
    {
        var presetId = Guid.NewGuid();
        _contractPresets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ContractPreset.Create(presetId, "My Preset", StrictnessRule.Locked, null, null, true));

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { ContractPresetId = presetId });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EndDateBeforeStartDate_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(
            ValidCommand() with { StartDate = EndDate, EndDate = StartDate });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(24.1)]
    [InlineData(25)]
    public async Task Validate_FixedDailyFloorHoursOutOfRange_IsInvalid(double floor)
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { FixedDailyFloorHours = floor });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FixedDailyFloorHoursNotWholeNumber_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { FixedDailyFloorHours = 3.5 });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FixedDailyFloorHoursNull_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { FixedDailyFloorHours = null });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_FixedDailyFloorExceedsWorkHoursWindow_IsInvalid()
    {
        var command = ValidCommand() with
        {
            FixedDailyFloorHours = 6,
            WorkHoursStart = new TimeOnly(10, 0),
            WorkHoursEnd = new TimeOnly(15, 0) // 5h window — a 6h floor could never fit
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FixedDailyFloorExactlyFillsWorkHoursWindow_IsValid()
    {
        var command = ValidCommand() with
        {
            FixedDailyFloorHours = 5,
            WorkHoursStart = new TimeOnly(10, 0),
            WorkHoursEnd = new TimeOnly(15, 0)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OnlyWorkHoursStartSet_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { WorkHoursStart = new TimeOnly(9, 0) });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_WorkHoursEndBeforeStart_IsInvalid()
    {
        var command = ValidCommand() with { WorkHoursStart = new TimeOnly(17, 0), WorkHoursEnd = new TimeOnly(9, 0) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BothWorkHoursSetCorrectly_IsValid()
    {
        var command = ValidCommand() with { WorkHoursStart = new TimeOnly(9, 0), WorkHoursEnd = new TimeOnly(17, 0) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_NeitherWorkHoursSet_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockPresetNotFound_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);
        var block = new NenContractBlockSpec(presetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Blocks = [block] });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ValidCommandWithBlocks_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow)));
        var block = new NenContractBlockSpec(presetId, At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Blocks = [block] });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OverlapsExistingContract_IsInvalid()
    {
        _checkOverlap.HandleAsync(Arg.Any<CheckNenContractDateRangeOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns("Other Contract");

        var result = await CreateValidator().ValidateAsync(ValidCommand());

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NoOverlap_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OverlapCheck_ReceivesCommandDateRange()
    {
        await CreateValidator().ValidateAsync(ValidCommand());

        await _checkOverlap.Received(1).HandleAsync(
            Arg.Is<CheckNenContractDateRangeOverlapQuery>(q => q.StartDate == StartDate && q.EndDate == EndDate),
            Arg.Any<CancellationToken>());
    }
}
