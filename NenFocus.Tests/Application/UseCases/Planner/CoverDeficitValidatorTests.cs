using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.UseCases.Planner.CoverDeficit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// The validator asks the contract (<c>CheckRedemptionPlacement</c>, tested in
/// <c>NenContractRedemptionPlacementTests</c>), then the placement checker on the slot that will
/// be saved — the capped one (Gap 106, decision 13; Design Review Rule 12).
/// </summary>
public class CoverDeficitValidatorTests
{
    private static readonly DateTime Now = CoverDeficitTestContext.Now;
    private readonly CoverDeficitTestContext _context = new();

    private CoverDeficitValidator CreateValidator() =>
        new(_context.Contracts, _context.Blocks, _context.Sessions, new BlockPlacementChecker(), _context.Clock);

    private CoverDeficitCommand Command(DateTime start, DateTime end) => new()
    {
        ContractId = _context.Contract.Id,
        PresetId = _context.PresetId,
        Slot = At.Slot(start, end)
    };

    private static string Wording(RedemptionPlacementOutcome outcome) =>
        RedemptionVerdictDto.From(outcome).Message!;

    [Fact]
    public async Task Validate_OwedAndFree_IsValid()
    {
        (await CreateValidator().ValidateAsync(Command(Now.AddHours(1), Now.AddHours(2)))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_UnknownContract_IsInvalid()
    {
        var command = Command(Now.AddHours(1), Now.AddHours(2)) with { ContractId = Guid.NewGuid() };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == RedemptionVerdictDto.ContractNotFound);
    }

    [Fact]
    public async Task Validate_OutsideTheContract_IsInvalidWithItsWording()
    {
        var result = await CreateValidator().ValidateAsync(Command(Now.AddDays(10), Now.AddDays(10).AddHours(1)));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == Wording(RedemptionPlacementOutcome.OutsideContract));
    }

    [Fact]
    public async Task Validate_NothingOfThePresetOwed_IsInvalidWithItsWording()
    {
        var command = Command(Now.AddHours(1), Now.AddHours(2)) with { PresetId = Guid.NewGuid() };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == Wording(RedemptionPlacementOutcome.NothingOwed));
    }

    [Fact]
    public async Task Validate_InThePast_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(Command(Now.AddHours(-1), Now.AddMinutes(-30)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_AFocusBlockAlreadyThere_IsInvalid()
    {
        _context.CalendarHas(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(1), Now.AddHours(2))));

        var result = await CreateValidator().ValidateAsync(Command(Now.AddHours(1), Now.AddHours(2)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LongerThanTheDebt_ChecksTheCappedSlot()
    {
        // 60 minutes owed; a 2-hour tile is saved as its first hour. A Focus block in the second
        // hour is not in the way of what's saved (Rule 12: check the value that gets saved).
        _context.CalendarHas(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(2), Now.AddHours(3))));

        var result = await CreateValidator().ValidateAsync(Command(Now.AddHours(1), Now.AddHours(3)));

        result.IsValid.Should().BeTrue();
    }
}
