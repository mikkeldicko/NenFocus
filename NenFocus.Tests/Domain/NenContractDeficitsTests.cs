using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

/// <summary>
/// The deficit ledger (Gap 106, decision 1): every missed or late block of the contract still
/// owed, net of what redemptions deliver. Each block answers from its own stamps (Gap 95); a
/// covering block's claim counts only for what it delivered once it is final (Rules 13, 18).
/// </summary>
public class NenContractDeficitsTests
{
    private static readonly DateTime Now = new(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly NenContract Contract = SealContract();

    private static NenContract SealContract()
    {
        var monday = new DateOnly(2026, 1, 5);
        var rules = TestRules.Of(StrictnessRule.Locked, null);
        var plan = new Dictionary<DateOnly, double>();
        return NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(monday, monday.AddDays(2), rules.FloorRule, plan), plan, At.Utc(2026, 1, 4, 12), At.UtcZone);
    }

    private static FocusBlock Committed(DateTime start, DateTime end) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(start, end), membership: TestContracts.Committed(Contract.Id));

    private static FocusBlock Redemption(FocusBlock missed, DateTime start, DateTime end, WholeMinutes minutesCovered) =>
        FocusBlock.CreateWithReceipt(Guid.NewGuid(), missed.BlockPresetId, At.Slot(start, end),
            title: null, membership: TestContracts.Redemption(Contract.Id), receipt: new RedemptionReceipt([new RedemptionEntry(missed.Id, minutesCovered)]));

    private static FocusBlock Worked(FocusBlock block, double ritualAfterStart, double? endedAfterStart) =>
        TestBlocks.Worked(block, ritualAfterStart, endedAfterStart);

    private static IReadOnlyList<OutstandingDeficit> Deficits(params Block[] blocks) =>
        Contract.Deficits(blocks, At.Utc(Now));

    [Fact]
    public void NoBlocks_OwesNothing()
    {
        Deficits().Should().BeEmpty();
    }

    [Fact]
    public void AnotherContractsBlock_IsRefused()
    {
        var stranger = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-3), Now.AddHours(-1)),
            membership: TestContracts.Committed(Guid.NewGuid()));

        var act = () => Deficits(stranger);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MissedBonusBlock_OwesNothing()
    {
        // Gap 97: a Bonus block never counts toward the floor, so missing one creates no deficit.
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-3), Now.AddHours(-1)),
            membership: TestContracts.Bonus(Contract.Id));

        Deficits(bonus).Should().BeEmpty();
    }

    [Fact]
    public void CompletedOnTimeBlock_OwesNothing()
    {
        Deficits(Worked(Committed(Now.AddHours(-3), Now.AddHours(-1)), 0, 120)).Should().BeEmpty();
    }

    [Fact]
    public void OpenBlock_OwesNothingYet()
    {
        Deficits(Committed(Now.AddHours(-1), Now.AddHours(1))).Should().BeEmpty();
    }

    [Fact]
    public void StartedLateSessionStillRunning_OwesNothingYet()
    {
        // Rule 18: still running, its final credit isn't knowable yet.
        Deficits(Worked(Committed(Now.AddMinutes(-10), Now.AddMinutes(50)), 10, null)).Should().BeEmpty();
    }

    [Fact]
    public void MissedBlock_OwesItAll()
    {
        var block = Committed(Now.AddHours(-3), Now.AddHours(-1));

        Deficits(block).Should().ContainSingle().Which.Should().Be(new OutstandingDeficit(
            block.Id, block.BlockPresetId, block.Scheduled, FocusBlockOutcome.Missed, Owed: WholeMinutes.Of(120), Remaining: WholeMinutes.Of(120)));
    }

    [Fact]
    public void StartedLateBlockRanToItsEnd_OwesTheLateStart()
    {
        var deficit = Deficits(Worked(Committed(Now.AddHours(-3), Now.AddHours(-1)), 20, 120)).Should().ContainSingle().Subject;

        deficit.Outcome.Should().Be(FocusBlockOutcome.StartedLate);
        deficit.Owed.Should().Be(WholeMinutes.Of(20));
        deficit.Remaining.Should().Be(WholeMinutes.Of(20));
    }

    [Fact]
    public void EndedEarlyBlock_OwesScheduledMinusDelivered()
    {
        var deficit = Deficits(Worked(Committed(Now.AddHours(-3), Now.AddHours(-1)), 0, 45)).Should().ContainSingle().Subject;

        deficit.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        deficit.Remaining.Should().Be(WholeMinutes.Of(75));
    }

    [Fact]
    public void StartedLateSessionRanPastTheBlocksEnd_OwesWhatWasntCredited()
    {
        // 40 late, ran 150 from there: credit stops at the block's end (80 of 120) — Session.Complete
        // caps the end at the block's end (Gap 114, D6), so the block's frozen work never runs past it.
        var block = Committed(Now.AddHours(-3), Now.AddHours(-1));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Goal"), block, At.Utc(Now.AddHours(-3).AddMinutes(40)));
        session.Complete(At.Utc(Now.AddHours(-3).AddMinutes(190)), block);

        Deficits(block).Should().ContainSingle().Which.Remaining.Should().Be(WholeMinutes.Of(40));
    }

    [Fact]
    public void MissedBlockFullyCoveredByAPendingRedemption_IsSettled()
    {
        var missed = Committed(Now.AddHours(-3), Now.AddHours(-1));

        Deficits(missed, Redemption(missed, Now.AddHours(1), Now.AddHours(3), WholeMinutes.Of(120))).Should().BeEmpty();
    }

    [Fact]
    public void RedemptionBlockThatStartedLate_IsNotADeficitOfItsOwn()
    {
        var missed = Committed(Now.AddHours(-5), Now.AddHours(-3));
        // 130 minutes long, 10 late, ran to the end: 120 credited, covering it all.
        var redemption = Worked(Redemption(missed, Now.AddMinutes(-150), Now.AddMinutes(-20), WholeMinutes.Of(120)), 10, 130);

        Deficits(missed, redemption).Should().BeEmpty();
    }

    [Fact]
    public void PartiallyCovered_RemainingIsReduced()
    {
        var missed = Committed(Now.AddHours(-3), Now.AddHours(-1));

        var deficit = Deficits(missed, Redemption(missed, Now.AddHours(1), Now.AddHours(2), WholeMinutes.Of(50))).Should().ContainSingle().Subject;

        deficit.Owed.Should().Be(WholeMinutes.Of(120));
        deficit.Remaining.Should().Be(WholeMinutes.Of(70));
    }

    [Fact]
    public void CoveringRedemptionMissed_TheDeficitReopens()
    {
        // Rule 13: a redemption that never happened pays nothing.
        var missed = Committed(Now.AddHours(-5), Now.AddHours(-3));

        Deficits(missed, Redemption(missed, Now.AddHours(-2), Now.AddHours(-1), WholeMinutes.Of(60)))
            .Should().ContainSingle().Which.Remaining.Should().Be(WholeMinutes.Of(120));
    }

    [Fact]
    public void CoveringRedemptionEndedEarly_PaysOnlyWhatItDelivered()
    {
        var missed = Committed(Now.AddHours(-5), Now.AddHours(-3));
        var redemption = Worked(Redemption(missed, Now.AddHours(-2), Now.AddHours(-1), WholeMinutes.Of(60)), 0, 20);

        Deficits(missed, redemption).Should().ContainSingle().Which.Remaining.Should().Be(WholeMinutes.Of(100));
    }

    [Fact]
    public void CoveringRedemptionStillRunning_ItsClaimIsTrusted()
    {
        // Rule 18: not final yet, so the whole claim counts until its session ends.
        var missed = Committed(Now.AddHours(-5), Now.AddHours(-3));
        var redemption = Worked(Redemption(missed, Now.AddMinutes(-20), Now.AddMinutes(40), WholeMinutes.Of(60)), 0, null);

        Deficits(missed, redemption).Should().ContainSingle().Which.Remaining.Should().Be(WholeMinutes.Of(60));
    }

    [Fact]
    public void DeficitOfExactlyOneMinute_IsOwed()
    {
        Deficits(Worked(Committed(Now.AddHours(-3), Now.AddHours(-1)), 0, 119))
            .Should().ContainSingle().Which.Remaining.Should().Be(WholeMinutes.Of(1));
    }

    [Fact]
    public void EndedSecondsShortOfAMinute_OwesWholeMinutes()
    {
        // 1 min 37 s short: credit rounds down to 118 whole minutes, so 2 are owed.
        Deficits(Worked(Committed(Now.AddHours(-3), Now.AddHours(-1)), 0, 120 - 97 / 60.0))
            .Should().ContainSingle().Which.Remaining.Should().Be(WholeMinutes.Of(2));
    }

    [Fact]
    public void SeveralDeficits_OldestFirst()
    {
        var later = Committed(Now.AddMinutes(-90), Now.AddHours(-1));
        var earlier = Committed(Now.AddHours(-5), Now.AddHours(-4));

        Deficits(later, earlier).Select(d => d.MissedBlockId).Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public void Gap33_AnEarlyEndToTheMillisecond_CoveredByItsSuggestedTile_IsPaidExactly()
    {
        // Gap 33's case: a session ended to the millisecond (31.906 min into a 60 min block). Credit
        // rounds down to 31, so 29 whole minutes are owed; the suggested tile covers exactly that, and
        // the ledger clears to nothing — no tolerance needed, because no fraction exists (Gap 92, V10).
        var missed = Worked(Committed(Now.AddHours(-5), Now.AddHours(-4)), 0, 31.906);
        var owed = Deficits(missed).Should().ContainSingle().Subject.Remaining;
        owed.Should().Be(WholeMinutes.Of(29));

        var tile = TimeSlot.Covering(At.Utc(Now.AddHours(1)), owed);
        var redemption = Redemption(missed, Now.AddHours(1), Now.AddHours(1) + tile.Length, owed);

        Deficits(missed, redemption).Should().BeEmpty();
    }
}
