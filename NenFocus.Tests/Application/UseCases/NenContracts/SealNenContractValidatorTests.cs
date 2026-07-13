using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
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

    private SealNenContractValidator CreateValidator() => new(_presets);

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
    public async Task Validate_EndDateBeforeStartDate_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(
            ValidCommand() with { StartDate = EndDate, EndDate = StartDate });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24.1)]
    public async Task Validate_FixedDailyFloorHoursOutOfRange_IsInvalid(double floor)
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { FixedDailyFloorHours = floor });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FixedDailyFloorHoursNull_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand() with { FixedDailyFloorHours = null });

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
    public async Task Validate_BlockEndNotAfterStart_IsInvalid()
    {
        var block = new NenContractBlockSpec(Guid.NewGuid(),
            new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 9, 0, 0), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Blocks = [block] });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockPresetNotFound_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);
        var block = new NenContractBlockSpec(presetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Blocks = [block] });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ValidCommandWithBlocks_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var block = new NenContractBlockSpec(presetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0), IsFocus: true);

        var result = await CreateValidator().ValidateAsync(ValidCommand() with { Blocks = [block] });

        result.IsValid.Should().BeTrue();
    }
}
