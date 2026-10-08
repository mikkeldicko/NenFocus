using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

/// <summary>
/// Attribution in the Domain (Gap 106, decisions 1, 12–15): a redemption block may be placed only
/// inside its own contract, on a day the redemption rule allows, while something of its preset is
/// owed. Its receipt claims the oldest deficit first, and its slot is capped to what it covers.
/// </summary>
public class NenContractRedemptionPlacementTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    private static readonly Guid PresetId = Guid.NewGuid();

    private static NenContract Contract(
        RedemptionPlacementPolicy policy = RedemptionPlacementPolicy.CurrentDayOnly,
        DateOnly? from = null, DateOnly? to = null, UserTimeZone? timeZone = null) =>
        TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(from ?? Today.AddDays(-7), to ?? Today.AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.AddDays(-30)), timeZone: timeZone ?? At.UtcZone,
            redemptionPlacementPolicy: policy);

    /// <summary>A Committed block of <paramref name="contract"/>, missed: it ended before now, never worked.</summary>
    private static FocusBlock Missed(NenContract contract, DateTime start, double minutes, Guid? presetId = null) =>
        FocusBlock.Create(Guid.NewGuid(), presetId ?? PresetId, At.Slot(start, start.AddMinutes(minutes)),
            membership: TestContracts.Committed(contract.Id));

    private static RedemptionVerdict Check(NenContract contract, TimeSlot slot, IEnumerable<PlannedBlock> blocks,
        DateTime? now = null, UserTimeZone? timeZone = null) =>
        contract.CheckRedemptionPlacement(PresetId, slot, blocks, At.Utc(now ?? Now), timeZone ?? At.UtcZone);

    // ── Refusals, in order ──

    [Fact]
    public void OutsideTheContractsPeriod_IsOutsideContract()
    {
        var contract = Contract(from: Today, to: Today.AddDays(1));
        var missed = Missed(contract, Now.AddHours(-3), 60);

        Check(contract, At.Slot(Now.AddDays(5), Now.AddDays(5).AddHours(1)), [missed])
            .Outcome.Should().Be(RedemptionPlacementOutcome.OutsideContract);
    }

    [Fact]
    public void InsideAnotherContractsPeriod_IsOutsideContract()
    {
        // Decision 12: a Cover Deficit tile pays only the contract whose button it came from —
        // landing in another contract's period is refused, never a quiet payment of that debt.
        var mine = Contract(from: Today.AddDays(-2), to: Today);
        var theirs = Contract(from: Today.AddDays(1), to: Today.AddDays(3));
        var missed = Missed(mine, Now.AddHours(-3), 60);
        var inTheirs = At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1));
        theirs.Governs(inTheirs, At.UtcZone).Should().BeTrue();

        Check(mine, inTheirs, [missed]).Outcome.Should().Be(RedemptionPlacementOutcome.OutsideContract);
    }

    [Fact]
    public void StraddlingTheContractsEnd_IsOutsideContract()
    {
        var contract = Contract(from: Today.AddDays(-2), to: Today);
        var missed = Missed(contract, Now.AddHours(-3), 60);
        var endOfToday = Today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        Check(contract, At.Slot(endOfToday.AddMinutes(-30), endOfToday.AddMinutes(30)), [missed])
            .Outcome.Should().Be(RedemptionPlacementOutcome.OutsideContract);
    }

    [Fact]
    public void CurrentDayOnly_ProposedForTomorrow_IsWrongDay()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 60);

        Check(contract, At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1)), [missed])
            .Outcome.Should().Be(RedemptionPlacementOutcome.WrongDay);
    }

    [Fact]
    public void NothingOfThisPresetOwed_IsNothingOwed()
    {
        var contract = Contract();
        var otherPreset = Missed(contract, Now.AddHours(-3), 60, presetId: Guid.NewGuid());

        Check(contract, At.Slot(Now, Now.AddHours(1)), [otherPreset])
            .Outcome.Should().Be(RedemptionPlacementOutcome.NothingOwed);
    }

    [Fact]
    public void NoBlocksAtAll_IsNothingOwed()
    {
        Check(Contract(), At.Slot(Now, Now.AddHours(1)), []).Outcome.Should().Be(RedemptionPlacementOutcome.NothingOwed);
    }

    [Fact]
    public void DeficitAlreadyFullyCovered_IsNothingOwed()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 60);
        var earlier = contract.PlaceRedemptionBlock(Guid.NewGuid(), PresetId, At.Slot(Now.AddHours(1), Now.AddHours(2)),
            [missed], At.Utc(Now), At.UtcZone);

        Check(contract, At.Slot(Now.AddHours(3), Now.AddHours(4)), [missed, earlier])
            .Outcome.Should().Be(RedemptionPlacementOutcome.NothingOwed);
    }

    [Fact]
    public void Refused_CarriesNoReceiptAndNoSlot()
    {
        var verdict = Check(Contract(), At.Slot(Now, Now.AddHours(1)), []);

        verdict.Receipt.Should().Be(RedemptionReceipt.None);
        verdict.Slot.Should().BeNull();
        verdict.Membership.Should().BeNull();
        verdict.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void Placed_CarriesTheRedemptionMembershipTheBlockWouldGet()
    {
        // The CoverDeficit validator checks placement as the block will be saved (Rule 12), and
        // only the Domain makes a Redemption membership.
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 60);

        var membership = Check(contract, At.Slot(Now, Now.AddHours(1)), [missed]).Membership;

        membership!.IsRedemption.Should().BeTrue();
        membership.ContractId.Should().Be(contract.Id);
    }

    // ── Placed: the receipt ──

    [Fact]
    public void SameDayDeficit_IsCovered()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 40);

        var verdict = Check(contract, At.Slot(Now, Now.AddHours(1)), [missed]);

        verdict.Outcome.Should().Be(RedemptionPlacementOutcome.Placed);
        verdict.Receipt.Entries.Should().Equal(new RedemptionEntry(missed.Id, WholeMinutes.Of(40)));
    }

    [Fact]
    public void CurrentDayOnly_DeficitMissedOnAnEarlierDay_IsStillClaimableToday()
    {
        // The day rule compares the proposed day with today only — never with the missed block's day.
        var contract = Contract();
        var missed = Missed(contract, Now.AddDays(-1), 40);

        Check(contract, At.Slot(Now, Now.AddHours(1)), [missed])
            .Receipt.Entries.Should().Equal(new RedemptionEntry(missed.Id, WholeMinutes.Of(40)));
    }

    [Fact]
    public void AnywhereInContract_ProposedOnALaterDay_IsCovered()
    {
        var contract = Contract(RedemptionPlacementPolicy.AnywhereInContract);
        var missed = Missed(contract, Now.AddDays(-3), 40);

        Check(contract, At.Slot(Now.AddDays(2), Now.AddDays(2).AddHours(1)), [missed])
            .Outcome.Should().Be(RedemptionPlacementOutcome.Placed);
    }

    [Fact]
    public void TheDayRuleUsesTheLocalDay_NotTheUtcDate()
    {
        // Now is 01:00 UTC on the 6th — 20:00 on the 5th at UTC-5. A tile at 23:00 UTC on the 5th
        // is 18:00 local the same day: allowed, though their UTC dates differ.
        var minusFive = new UserTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5"));
        var contract = Contract(timeZone: minusFive);
        var missed = Missed(contract, Now.AddHours(-3), 40);
        var now = new DateTime(2026, 1, 6, 1, 0, 0, DateTimeKind.Utc);
        var proposed = new DateTime(2026, 1, 5, 23, 0, 0, DateTimeKind.Utc);

        Check(contract, At.Slot(proposed, proposed.AddHours(1)), [missed], now, minusFive)
            .Outcome.Should().Be(RedemptionPlacementOutcome.Placed);
    }

    [Fact]
    public void TwoDeficits_OldestFirst_SurplusClaimsNothing()
    {
        var contract = Contract();
        var newer = Missed(contract, Now.AddHours(-2), 50);
        var older = Missed(contract, Now.AddHours(-5), 30);

        // 100 minutes: 30 to the older, 50 to the newer, 20 left over.
        var verdict = Check(contract, At.Slot(Now, Now.AddMinutes(100)), [newer, older]);

        verdict.Receipt.Entries.Should().Equal(new RedemptionEntry(older.Id, WholeMinutes.Of(30)), new RedemptionEntry(newer.Id, WholeMinutes.Of(50)));
        verdict.Slot.Should().Be(At.Slot(Now, Now.AddMinutes(80)));
    }

    [Fact]
    public void TwoDeficits_RoomRunsOutPartWayThroughTheSecond()
    {
        // Gap 113, S3: the room the first claim uses is gone for the second.
        var contract = Contract();
        var newer = Missed(contract, Now.AddHours(-2), 50);
        var older = Missed(contract, Now.AddHours(-5), 40);

        Check(contract, At.Slot(Now, Now.AddMinutes(60)), [newer, older])
            .Receipt.Entries.Should().Equal(new RedemptionEntry(older.Id, WholeMinutes.Of(40)), new RedemptionEntry(newer.Id, WholeMinutes.Of(20)));
    }

    [Fact]
    public void TwoDeficits_RoomUsedUpByTheFirst_ClaimsNothingOfTheSecond()
    {
        var contract = Contract();
        var newer = Missed(contract, Now.AddHours(-2), 50);
        var older = Missed(contract, Now.AddHours(-5), 40);

        Check(contract, At.Slot(Now, Now.AddMinutes(40)), [newer, older])
            .Receipt.Entries.Should().Equal(new RedemptionEntry(older.Id, WholeMinutes.Of(40)));
    }

    [Fact]
    public void AnywhereInContract_TheFirstAndLastDaysAreInside()
    {
        // Gap 113, S7: the period's own edges are eligible days.
        var contract = Contract(RedemptionPlacementPolicy.AnywhereInContract, from: Today.AddDays(-2), to: Today.AddDays(2));

        contract.CheckRedemptionEligibility(Today.AddDays(-2), Today).Should().Be(RedemptionEligibility.Allowed);
        contract.CheckRedemptionEligibility(Today.AddDays(2), Today).Should().Be(RedemptionEligibility.Allowed);
        contract.CheckRedemptionEligibility(Today.AddDays(-3), Today).Should().Be(RedemptionEligibility.WrongDay);
        contract.CheckRedemptionEligibility(Today.AddDays(3), Today).Should().Be(RedemptionEligibility.WrongDay);
    }

    [Fact]
    public void PartlyCoveredDeficit_ClaimsOnlyWhatIsLeft()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 60);
        var earlier = contract.PlaceRedemptionBlock(Guid.NewGuid(), PresetId, At.Slot(Now.AddHours(1), Now.AddMinutes(100)),
            [missed], At.Utc(Now), At.UtcZone);

        Check(contract, At.Slot(Now.AddHours(3), Now.AddHours(4)), [missed, earlier])
            .Receipt.Entries.Should().Equal(new RedemptionEntry(missed.Id, WholeMinutes.Of(20)));
    }

    // ── Placed: the slot ──

    [Fact]
    public void LongerThanTheDebt_IsCappedAndSaysSo()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 45);

        var verdict = Check(contract, At.Slot(Now, Now.AddHours(2)), [missed]);

        verdict.Slot.Should().Be(At.Slot(Now, Now.AddMinutes(45)));
        verdict.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void ShorterThanTheDebt_KeepsItsSlot()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 90);

        var verdict = Check(contract, At.Slot(Now, Now.AddHours(1)), [missed]);

        verdict.Slot.Should().Be(At.Slot(Now, Now.AddHours(1)));
        verdict.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void ASuggestionSizedTile_IsNotCapped()
    {
        // Decision 15's intent — an ordinary Cover Deficit save shows no notice — now holds by
        // construction: the suggestion is sized exactly to the whole-minute debt (Gap 92, V10), so
        // nothing is trimmed. Any trim at all is whole minutes, and worth telling.
        var contract = Contract();
        var missed = TestBlocks.Worked(Missed(contract, Now.AddHours(-3), 60), 0, 60 - 97 / 60.0); // owes 2 whole minutes

        var verdict = Check(contract, TimeSlot.Covering(At.Utc(Now), WholeMinutes.Of(2)), [missed]);

        verdict.Slot.Should().Be(At.Slot(Now, Now.AddMinutes(2)));
        verdict.WasCapped.Should().BeFalse();
    }

    // ── PlaceRedemptionBlock ──

    [Fact]
    public void PlaceRedemptionBlock_BuildsARedemptionBlockOfThisContract_WithTheVerdictsReceiptAndSlot()
    {
        var contract = Contract();
        var missed = Missed(contract, Now.AddHours(-3), 45);
        var blockId = Guid.NewGuid();

        var block = contract.PlaceRedemptionBlock(blockId, PresetId, At.Slot(Now, Now.AddHours(2)), [missed], At.Utc(Now), At.UtcZone);

        block.Id.Should().Be(blockId);
        block.BlockPresetId.Should().Be(PresetId);
        block.IsRedemption.Should().BeTrue();
        block.Membership!.ContractId.Should().Be(contract.Id);
        block.Scheduled.Should().Be(At.Slot(Now, Now.AddMinutes(45)));
        block.Receipt.Entries.Should().Equal(new RedemptionEntry(missed.Id, WholeMinutes.Of(45)));
    }

    [Fact]
    public void PlaceRedemptionBlock_Refused_Throws()
    {
        var contract = Contract();

        var act = () => contract.PlaceRedemptionBlock(Guid.NewGuid(), PresetId, At.Slot(Now, Now.AddHours(1)), [], At.Utc(Now), At.UtcZone);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AnotherContractsBlock_IsRefused()
    {
        var contract = Contract();
        var stranger = Missed(Contract(), Now.AddHours(-3), 45);

        var act = () => Check(contract, At.Slot(Now, Now.AddHours(1)), [stranger]);

        act.Should().Throw<ArgumentException>();
    }
}
