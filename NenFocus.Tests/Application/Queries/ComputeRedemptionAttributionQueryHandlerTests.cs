using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class ComputeRedemptionAttributionQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public ComputeRedemptionAttributionQueryHandlerTests()
    {
        // Identity conversion by default — every existing scenario below reasons purely in
        // UTC, so "local" trivially equals the UTC value unless a test overrides it.
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetNow(Now);
    }

    private ComputeRedemptionAttributionQueryHandler CreateHandler() => new(_contracts, _deficits, _clock);

    private static NenContract SameDayContract(Guid id) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Today.AddDays(-1), Today.AddDays(7), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Today.AddDays(-1).ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone,
            redemptionPlacementPolicy: RedemptionPlacementPolicy.CurrentDayOnly);

    private static NenContract AnyDayContract(Guid id) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.PeriodTimeBudget,
            calendar: TestCalendars.For(Today.AddDays(-7), Today.AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Today.AddDays(-7).ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone,
            contractFloor: new ContractFloor(10),
            redemptionPlacementPolicy: RedemptionPlacementPolicy.AnywhereInContract);

    private static FocusBlockDeficitDto Deficit(Guid presetId, DateTime scheduledStart, double remaining, Guid? focusBlockId = null) =>
        new()
        {
            FocusBlockId = focusBlockId ?? Guid.NewGuid(),
            BlockPresetId = presetId,
            Scheduled = At.Slot(scheduledStart, scheduledStart.AddHours(1)),
            Outcome = FocusBlockOutcome.Missed,
            RemainingDeficitMinutes = remaining,
            PlannedMinutes = 60,
            CompletedMinutes = 0
        };

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsNone()
    {
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_EligibleSameDayDeficit_IsCovered()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId && e.MinutesCovered == 40);
    }

    [Fact]
    public async Task HandleAsync_DifferentPreset_ExcludedFromEligibility()
    {
        var contractId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(Guid.NewGuid(), Now, remaining: 40);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CurrentDayOnlyContract_DeficitMissedOnADifferentDay_StillEligibleToday()
    {
        // The bug this feature exists to fix: eligibility no longer compares the proposed
        // placement day against the missed block's own day at all — only against today. A
        // deficit from yesterday (or longer ago) stays claimable today.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now.AddDays(-1), remaining: 40); // missed yesterday
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId && e.MinutesCovered == 40);
    }

    [Fact]
    public async Task HandleAsync_CurrentDayOnlyContract_ProposedOnADifferentDayThanToday_WrongDay()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        // Proposing the block for tomorrow, even though "now" (today) is still today.
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1)), null),
            CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UsesLocalCalendarDay_NotRawUtcDate_ForRedemptionEligibility()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);

        // "Now" (today) is 1:00 AM UTC Jan 2 — but 8:00 PM local (UTC-5) on Jan 1.
        var nowUtc = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc);

        // Proposed block at 11:00 PM UTC Jan 1 — 6:00 PM local (UTC-5), the *same* local day
        // as "now" even though their raw UTC dates differ. A fix that forgot to convert
        // would compare Jan 1 (UTC) against Jan 2 (UTC) and wrongly report WrongDay.
        var proposedStartUtc = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);

        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        _clock.SetNow(nowUtc);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(proposedStartUtc, proposedStartUtc.AddHours(1)), null),
            CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId);
    }

    [Fact]
    public async Task HandleAsync_ExcludeBlockId_RemovesThatCandidate()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId, ExcludeCoverageFromBlockId: deficit.FocusBlockId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddHours(1)), deficit.FocusBlockId), CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_EditingAnAlreadyCoveringBlock_ExcludesItsOwnStaleEntryFromCoverageSum()
    {
        // Reproduces the live bug: editing an already-covering redemption block used to see
        // its own stale, pre-edit RedemptionEntries (still in the DB until the edit itself
        // is saved) as "already covered by someone else" — GetNenContractDeficitsQuery must
        // be asked to exclude the block being edited from that sum, or the recompute
        // collapses toward zero on every edit.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var editingBlockId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId, ExcludeCoverageFromBlockId: editingBlockId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddMinutes(8)), editingBlockId), CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId && e.MinutesCovered == 8);
    }

    [Fact]
    public async Task HandleAsync_TwoDeficitsSamePreset_OldestFirstWithSpillover()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var older = Deficit(presetId, Now.AddHours(-3), remaining: 30);
        var newer = Deficit(presetId, Now.AddHours(-1), remaining: 50);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[newer, older]); // deliberately out of order

        // A 100-minute block: fully covers the 30-min older deficit, spills 50 into the
        // newer one, and the remaining 20 minutes of surplus attributes to nothing further.
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddMinutes(100)), null), CancellationToken.None);

        result.Entries.Should().HaveCount(2);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == older.FocusBlockId && e.MinutesCovered == 30);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == newer.FocusBlockId && e.MinutesCovered == 50);
    }

    // ── The final slot (Gap 88 phase 8: attribution is the one place the cap is computed) ──

    [Fact]
    public async Task HandleAsync_BlockLongerThanItCovers_CapsTheSlotAndReportsCapped()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[Deficit(presetId, Now.AddHours(-2), remaining: 45)]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId, At.Slot(Now, Now.AddHours(2)), null), CancellationToken.None);

        result.Slot.Should().Be(At.Slot(Now, Now.AddMinutes(45)));
        result.WasCapped.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BlockFullyCovered_KeepsTheRequestedSlot()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[Deficit(presetId, Now.AddHours(-2), remaining: 90)]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId, At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Slot.Should().Be(At.Slot(Now, Now.AddHours(1)));
        result.WasCapped.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NoEligibleDeficit_KeepsTheRequestedSlot()
    {
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now, Now.AddHours(2)), null), CancellationToken.None);

        result.Slot.Should().Be(At.Slot(Now, Now.AddHours(2)));
        result.WasCapped.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NoDeficitInTheContract_CoversNothing()
    {
        // Editing a block that's already tied to a contract but doesn't happen to cover
        // anything right now must not silently lose its contract association.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_AsksOnlyTheContractItIsGiven()
    {
        // Even though another in-effect contract would have an eligible match, an explicit
        // ContractId must never search outside of it.
        var scopedContractId = Guid.NewGuid();
        var otherContractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var scopedContract = SameDayContract(scopedContractId);
        var otherDeficit = Deficit(presetId, Now, remaining: 40);
        _contracts.GetByIdAsync(scopedContractId, Arg.Any<CancellationToken>()).Returns(scopedContract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(scopedContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(otherContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[otherDeficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(scopedContractId, presetId,At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().BeEmpty();
        await _contracts.DidNotReceive().GetAllInEffectAsync(Arg.Any<CancellationToken>());
    }

    // ── Carry-forward (Option D): existing entries are continuations, never re-gated ──

    [Fact]
    public async Task HandleAsync_ExistingEntry_CarriedForwardEvenWhenProposedDayNoLongerEligible()
    {
        // The whole point of carry-forward: a block already covering a deficit keeps that
        // coverage when moved to a day CurrentDayOnly would refuse for a *new* claim.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var missedBlockId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now.AddDays(-2), remaining: 25, focusBlockId: missedBlockId);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var existingEntries = new[] { new RedemptionEntryDto(missedBlockId, 25) };
        // Proposed a day away from "today" — CurrentDayOnly would refuse this as a *new* claim.
        var proposedStart = Now.AddDays(1);
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(proposedStart, proposedStart.AddMinutes(25)), null, existingEntries),
            CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == missedBlockId && e.MinutesCovered == 25);
    }

    [Fact]
    public async Task HandleAsync_ExistingEntry_AmountRescalesToCurrentRemainingDeficit()
    {
        // The missed block's remaining deficit shrank since this entry was created (e.g.
        // another block covered part of it) — the carried entry rescales down, it doesn't
        // keep claiming its stale original amount.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var missedBlockId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var deficit = Deficit(presetId, Now, remaining: 10, focusBlockId: missedBlockId); // shrunk from 25 to 10
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var existingEntries = new[] { new RedemptionEntryDto(missedBlockId, 25) };
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddMinutes(25)), null, existingEntries),
            CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == missedBlockId && e.MinutesCovered == 10);
    }

    [Fact]
    public async Task HandleAsync_ExistingEntry_TargetNoLongerInDeficitList_DroppedNotCarried()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var missedBlockId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]); // fully covered elsewhere now

        var existingEntries = new[] { new RedemptionEntryDto(missedBlockId, 25) };
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddMinutes(25)), null, existingEntries),
            CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CarryForwardLeavesRoom_NewEligibleClaimFillsTheRest()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var carriedMissedBlockId = Guid.NewGuid();
        var contract = SameDayContract(contractId);
        var carried = Deficit(presetId, Now.AddDays(-1), remaining: 10, focusBlockId: carriedMissedBlockId);
        var fresh = Deficit(presetId, Now, remaining: 15);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[carried, fresh]);

        var existingEntries = new[] { new RedemptionEntryDto(carriedMissedBlockId, 10) };
        // A 25-minute block: 10 carried forward, 15 minutes of fresh capacity left over —
        // eligible today, so the new claim against `fresh` fills it.
        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddMinutes(25)), null, existingEntries),
            CancellationToken.None);

        result.Entries.Should().HaveCount(2);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == carriedMissedBlockId && e.MinutesCovered == 10);
        result.Entries.Should().ContainSingle(e => e.MissedBlockId == fresh.FocusBlockId && e.MinutesCovered == 15);
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudgetWithContractFloor_MissedOnADifferentDay_StillEligible()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = AnyDayContract(contractId);
        var deficit = Deficit(presetId, Now.AddDays(-3), remaining: 40);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);

        var result = await CreateHandler().HandleAsync(
            new ComputeRedemptionAttributionQuery(contractId, presetId,At.Slot(Now, Now.AddHours(1)), null), CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.MissedBlockId == deficit.FocusBlockId);
    }
}
