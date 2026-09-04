using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class EditNenContractValidatorTests
{
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly EndDate = new(2026, 6, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IContractPresetRepository _contractPresets = Substitute.For<IContractPresetRepository>();
    private readonly IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> _checkOverlap =
        Substitute.For<IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?>>();

    public EditNenContractValidatorTests()
    {
        _checkOverlap.HandleAsync(Arg.Any<CheckNenContractDateRangeOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
    }

    private EditNenContractValidator CreateValidator() => new(_contracts, _presets, _contractPresets, _checkOverlap);

    private static NenContract Seal(DateTime sealedAt) => NenContract.Seal(
        id: Guid.NewGuid(), name: "Original", goal: new NenContractGoal("Goal"),
        startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.DailyFlexibility,
        dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(), sealedAt: sealedAt);

    private static EditNenContractCommand BaseCommand(Guid contractId) => new()
    {
        ContractId = contractId,
        Name = "New Name",
        Goal = "New goal",
        StartDate = StartDate,
        EndDate = EndDate,
        StrictnessRule = StrictnessRule.Locked,
        AllowCancellation = true
    };

    [Fact]
    public async Task Validate_ContractNotFound_IsInvalid()
    {
        var id = Guid.NewGuid();
        _contracts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateValidator().ValidateAsync(BaseCommand(id));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ScheduledContract_IsValid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(BaseCommand(contract.Id));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractPresetIdDoesNotExist_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var presetId = Guid.NewGuid();
        _contractPresets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((ContractPreset?)null);

        var result = await CreateValidator().ValidateAsync(BaseCommand(contract.Id) with { ContractPresetId = presetId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ActiveContract_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(BaseCommand(contract.Id));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyName_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(BaseCommand(contract.Id) with { Name = "" });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EndBeforeStart_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(
            BaseCommand(contract.Id) with { StartDate = EndDate, EndDate = StartDate });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_OverlapCheck_ExcludesTheContractBeingEdited()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        await CreateValidator().ValidateAsync(BaseCommand(contract.Id));

        await _checkOverlap.Received(1).HandleAsync(
            Arg.Is<CheckNenContractDateRangeOverlapQuery>(q => q.ExcludeContractId == contract.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_DateRangeOverlapsAnotherContract_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _checkOverlap.HandleAsync(Arg.Any<CheckNenContractDateRangeOverlapQuery>(), Arg.Any<CancellationToken>())
            .Returns("Some Other Contract");

        var result = await CreateValidator().ValidateAsync(BaseCommand(contract.Id));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(24.1)]
    public async Task Validate_FixedDailyFloorHoursOutOfRange_IsInvalid(double floor)
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(BaseCommand(contract.Id) with { FixedDailyFloorHours = floor });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FixedDailyFloorExceedsWorkHoursWindow_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var command = BaseCommand(contract.Id) with
        {
            FixedDailyFloorHours = 6,
            WorkHoursStart = new TimeOnly(10, 0),
            WorkHoursEnd = new TimeOnly(15, 0)
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_WorkHoursOnlyStartSet_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(
            BaseCommand(contract.Id) with { WorkHoursStart = new TimeOnly(9, 0) });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockPresetDoesNotExist_IsInvalid()
    {
        var contract = Seal(sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var command = BaseCommand(contract.Id) with
        {
            Blocks = [new NenFocus.Application.DTOs.NenContractBlockSpec(
                Guid.NewGuid(), StartDate.ToDateTime(TimeOnly.MinValue), StartDate.ToDateTime(new TimeOnly(1, 0)), IsFocus: true)]
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
