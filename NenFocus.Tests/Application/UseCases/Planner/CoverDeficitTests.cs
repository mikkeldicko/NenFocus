using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.DTOs;
using NenFocus.Application.UseCases.Planner.CoverDeficit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// Covering a deficit, run as the app runs it: real pipeline, real SQLite (Gap 110). The validator
/// asks the contract (<c>CheckRedemptionPlacement</c>, tested in <c>NenContractRedemptionPlacementTests</c>),
/// then placement on the slot that will be saved — the capped one (Gap 106, decision 13; Design
/// Review Rule 12). The contract builds the block (<c>PlaceRedemptionBlock</c>); one block, one
/// transaction. The setting: a contract in effect (1–9 January) owing 60 minutes of one preset —
/// a Committed block missed three hours before now.
/// </summary>
public class CoverDeficitTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly NenContract _contract = TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 9), new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(2025, 12, 31), timeZone: At.UtcZone);

    private readonly FocusBlock _missed;

    public CoverDeficitTests()
    {
        Clock.SetNow(Now);
        _missed = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Now.AddHours(-3), Now.AddHours(-2)),
            membership: TestContracts.Committed(_contract.Id));
    }

    private CoverDeficitCommand Command(DateTime start, DateTime end) => new()
    {
        ContractId = _contract.Id,
        PresetId = SeededFocusPresetId,
        Slot = At.Slot(start, end)
    };

    /// <summary>Stores the contract and its missed block, plus whatever else is already on the calendar.</summary>
    private Task GivenAsync(params PlannedBlock[] onTheCalendar) =>
        SeedAsync([_contract, _missed, .. onTheCalendar]);

    private async Task<(CoverDeficitResult Result, FocusBlock Saved)> CoverAsync(CoverDeficitCommand command)
    {
        await GivenAsync();
        var result = await RunAsync(command);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (result, (FocusBlock)(await FindAsync<PlannedBlock>(result.BlockId!.Value))!);
    }

    private async Task<CoverDeficitResult> ShouldBeRefused(CoverDeficitCommand command, string? message = null)
    {
        var before = await ReadAsync(context => context.Set<PlannedBlock>().CountAsync());

        var result = await RunAsync(command);

        result.Success.Should().BeFalse();
        if (message is not null) result.ErrorMessage.Should().Be(message);
        (await ReadAsync(context => context.Set<PlannedBlock>().CountAsync())).Should().Be(before);
        return result;
    }

    private static string Wording(RedemptionPlacementOutcome outcome) => RedemptionVerdictDto.From(outcome).Message!;

    // ── What gets stored ─────────────────────────────────────────────────────

    [Fact]
    public async Task OwedAndFree_StoresARedemptionBlockOfTheContract_HoldingItsReceipt()
    {
        var (result, saved) = await CoverAsync(Command(Now.AddHours(1), Now.AddHours(2)));

        result.WasCapped.Should().BeFalse();
        saved.IsRedemption.Should().BeTrue();
        saved.Membership!.ContractId.Should().Be(_contract.Id);
        saved.BlockPresetId.Should().Be(SeededFocusPresetId);
        saved.Scheduled.Should().Be(At.Slot(Now.AddHours(1), Now.AddHours(2)));
        saved.Receipt.Entries.Should().Equal(new RedemptionEntry(_missed.Id, WholeMinutes.Of(60)));
    }

    [Fact]
    public async Task LongerThanTheDebt_StoresTheCappedSlot_AndSaysSo()
    {
        var (result, saved) = await CoverAsync(Command(Now.AddHours(1), Now.AddHours(3)));

        result.WasCapped.Should().BeTrue();
        saved.Scheduled.Should().Be(At.Slot(Now.AddHours(1), Now.AddHours(2)));
    }

    [Fact]
    public async Task LongerThanTheDebt_OnlyTheCappedSlotMustBeFree()
    {
        // A Focus block in the second hour is not in the way of what's saved (Rule 12).
        var (_, saved) = await CoverWithAsync(
            FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Now.AddHours(2), Now.AddHours(3))),
            Command(Now.AddHours(1), Now.AddHours(3)));

        saved.Scheduled.Should().Be(At.Slot(Now.AddHours(1), Now.AddHours(2)));
    }

    private async Task<(CoverDeficitResult, FocusBlock)> CoverWithAsync(PlannedBlock onTheCalendar, CoverDeficitCommand command)
    {
        await GivenAsync(onTheCalendar);
        var result = await RunAsync(command);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (result, (FocusBlock)(await FindAsync<PlannedBlock>(result.BlockId!.Value))!);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownContract_IsRefused()
    {
        await GivenAsync();

        await ShouldBeRefused(Command(Now.AddHours(1), Now.AddHours(2)) with { ContractId = Guid.NewGuid() },
            RedemptionVerdictDto.ContractNotFound);
    }

    [Fact]
    public async Task OutsideTheContract_IsRefusedWithItsWording()
    {
        await GivenAsync();

        await ShouldBeRefused(Command(Now.AddDays(10), Now.AddDays(10).AddHours(1)),
            Wording(RedemptionPlacementOutcome.OutsideContract));
    }

    [Fact]
    public async Task NothingOfThePresetOwed_IsRefusedWithItsWording()
    {
        await GivenAsync();

        await ShouldBeRefused(Command(Now.AddHours(1), Now.AddHours(2)) with { PresetId = Guid.NewGuid() },
            Wording(RedemptionPlacementOutcome.NothingOwed));
    }

    [Fact]
    public async Task InThePast_IsRefused()
    {
        await GivenAsync();

        await ShouldBeRefused(Command(Now.AddHours(-1), Now.AddMinutes(-30)));
    }

    [Fact]
    public async Task OverAFocusBlockAlreadyThere_IsRefused()
    {
        await GivenAsync(FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Now.AddHours(1), Now.AddHours(2))));

        await ShouldBeRefused(Command(Now.AddHours(1), Now.AddHours(2)));
    }
}
