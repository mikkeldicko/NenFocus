using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

/// <summary>
/// Each block carries its own stamps — Ritual completed, session ended — the way
/// StartSession/EndSession leave them; the handler asks the block, never a session (Gap 95).
/// </summary>
public class GetNenContractDeficitsQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetNenContractDeficitsQueryHandlerTests()
    {
        _clock.SetNow(Now);
    }

    private GetNenContractDeficitsQueryHandler CreateHandler() => new(_blocks, _clock);

    private static FocusBlock CommittedBlock(DateTime start, DateTime end, Guid? presetId = null) =>
        FocusBlock.Create(Guid.NewGuid(), presetId ?? Guid.NewGuid(), At.Slot(start, end), membership: TestContracts.Committed(ContractId));

    private static FocusBlock RedemptionBlock(Guid presetId, DateTime start, DateTime end, Guid missedId, double minutesCovered) =>
        FocusBlock.Create(Guid.NewGuid(), presetId, At.Slot(start, end),
            membership: TestContracts.Redemption(ContractId), redemptionEntries: [new RedemptionEntry(missedId, minutesCovered)]);

    /// <summary>The Ritual completed <paramref name="ritualAfterStart"/> into the block; the
    /// session ended <paramref name="endedAfterStart"/> into it, or is still running.</summary>
    private static FocusBlock Worked(FocusBlock block, TimeSpan ritualAfterStart, TimeSpan? endedAfterStart)
    {
        var start = At.Start(block.Scheduled);
        block.MarkRitualCompleted(start + ritualAfterStart);
        if (endedAfterStart is { } ended) block.MarkSessionEnded(start + ended);
        return block;
    }

    private void ContractHas(params PlannedBlock[] blocks) =>
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)blocks);

    private Task<IReadOnlyList<NenFocus.Application.DTOs.FocusBlockDeficitDto>> Handle(Guid? excludeCoverageFrom = null) =>
        CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId, ExcludeCoverageFromBlockId: excludeCoverageFrom), CancellationToken.None);

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public async Task HandleAsync_NoFocusBlocks_ReturnsEmpty()
    {
        ContractHas();

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_MissedBonusBlock_OwesNothing()
    {
        // Gap 97: a Bonus block carries the contract's id but never counts toward its floor,
        // so missing one creates no deficit.
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-3), Now.AddHours(-1)),
            membership: TestContracts.Bonus(ContractId));
        ContractHas(bonus);

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CompletedOnTimeBlock_Excluded()
    {
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(0), Min(120)));

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_OpenBlock_Excluded()
    {
        ContractHas(CommittedBlock(Now.AddHours(-1), Now.AddHours(1)));

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_StartedLateSessionStillActive_ExcludedFromDeficit()
    {
        // The core case Rule 18 exists for: a 1-hour block started 10 minutes late is still
        // running, with no idea yet whether it'll finish on schedule or end early — its final
        // credited minutes aren't knowable, so it must not surface as a deficit (or a Cover
        // Deficit suggestion) while still in flight.
        ContractHas(Worked(CommittedBlock(Now.AddMinutes(-10), Now.AddMinutes(50)), Min(10), endedAfterStart: null));

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_SessionEndedButPostSessionNotAcknowledged_StillCountsTowardDeficit()
    {
        // Acknowledging the Post-Session screen is a separate, unrelated fact (it exists for
        // crash-recovery replay, not deficit finality) — once the session has actually ended,
        // the deficit is real and visible regardless of whether the user has dismissed that
        // screen yet. The block's stamps don't even know about acknowledgement.
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(0), Min(45)));

        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(75);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockEnded_FullDeficit()
    {
        var block = CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block
        ContractHas(block);

        var dto = (await Handle()).Should().ContainSingle().Subject;
        dto.FocusBlockId.Should().Be(block.Id);
        dto.Outcome.Should().Be(FocusBlockOutcome.Missed);
        dto.RemainingDeficitMinutes.Should().Be(120);
        dto.PlannedMinutes.Should().Be(120);
        dto.CompletedMinutes.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_StartedLateBlockRanToItsEnd_DeficitIsTheLateStart()
    {
        // 20 min late, then ran to the scheduled end: 100 credited of 120.
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(20), Min(120)));

        var dto = (await Handle()).Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.StartedLate);
        dto.RemainingDeficitMinutes.Should().Be(20);
        dto.PlannedMinutes.Should().Be(120);
        dto.CompletedMinutes.Should().Be(100);
    }

    [Fact]
    public async Task HandleAsync_EndedEarlyBlock_DeficitIsScheduledMinusActual()
    {
        // Started right on time, but quit after 45 of the 120 scheduled minutes.
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(0), Min(45)));

        var dto = (await Handle()).Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.RemainingDeficitMinutes.Should().Be(75); // 120 - 45 — the early-end gap is no longer silently lost
        dto.PlannedMinutes.Should().Be(120);
        dto.CompletedMinutes.Should().Be(45);
    }

    [Fact]
    public async Task HandleAsync_StartedLateSessionRanPastBlocksScheduledEnd_DeficitUsesCreditedNotRawElapsed()
    {
        // Block scheduled for 120 minutes; Ritual completed 40 min late. The session then ran
        // a full 150 minutes from that late start, 70 minutes past the block's own scheduled
        // end. Credit stops at the scheduled end (120 - 40 = 80), so time spent after the
        // window closed can't zero out a real deficit.
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(40), Min(190)));

        var dto = (await Handle()).Should().ContainSingle().Subject;
        dto.RemainingDeficitMinutes.Should().Be(40); // 120 - 80
        dto.CompletedMinutes.Should().Be(80);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockFullyCoveredByPendingRedemption_Excluded()
    {
        var missed = CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var redemption = RedemptionBlock(missed.BlockPresetId, Now.AddHours(1), Now.AddHours(3), missed.Id, 120);
        ContractHas(missed, redemption);

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RedemptionBlockItselfStartedLate_DoesNotAppearAsItsOwnDeficit()
    {
        // Reproduces the reported bug: a redemption block that itself runs a few minutes past
        // its own grace period (e.g. the Ritual took a moment) must not become a second,
        // uncoverable deficit — it fully covered the block it exists to redeem.
        var missed = CommittedBlock(Now.AddHours(-5), Now.AddHours(-3)); // 120 min deficit
        // 130 minutes long, started 10 late, ran to the end: 120 credited, covering it all.
        var redemption = Worked(
            RedemptionBlock(missed.BlockPresetId, Now.AddMinutes(-150), Now.AddMinutes(-20), missed.Id, 120),
            Min(10), Min(130));
        ContractHas(missed, redemption);

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ExcludeCoverageFromBlockId_ThatBlocksOwnEntryIsIgnored()
    {
        // Reproduces the live bug: recomputing a redemption block's coverage on edit must
        // not see the block's own stale, pre-edit RedemptionEntry as coverage from someone
        // else — without the exclusion, remainingDeficit collapses toward zero instead of
        // the true 120-minute deficit.
        var missed = CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var redemption = RedemptionBlock(missed.BlockPresetId, Now.AddHours(1), Now.AddHours(3), missed.Id, 120);
        ContractHas(missed, redemption);

        (await Handle(excludeCoverageFrom: redemption.Id)).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(120);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockPartiallyCoveredByRedemption_RemainingDeficitReduced()
    {
        var missed = CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var redemption = RedemptionBlock(missed.BlockPresetId, Now.AddHours(1), Now.AddHours(2), missed.Id, 50);
        ContractHas(missed, redemption);

        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(70); // 120 - 50
    }

    [Fact]
    public async Task HandleAsync_CoveringRedemptionBlockItselfMissed_OriginalDeficitReopens()
    {
        // Rule 13's second half: a redemption block that never actually happened must not
        // keep "paying off" the deficit it was scheduled to cover — the original block's
        // deficit reopens in full.
        var missed = CommittedBlock(Now.AddHours(-5), Now.AddHours(-3)); // 120 min deficit
        var redemption = RedemptionBlock(missed.BlockPresetId, Now.AddHours(-2), Now.AddHours(-1), missed.Id, 60);
        ContractHas(missed, redemption);

        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(120); // full deficit, not 60
    }

    [Fact]
    public async Task HandleAsync_CoveringRedemptionBlockEndedEarly_CoverageCappedAtActualCredit()
    {
        // A redemption block scheduled to cover 60 minutes but that itself only delivered 20
        // real credited minutes (ended early) must not get credit for the full 60 it was
        // planned for — only its own actual outcome counts.
        var missed = CommittedBlock(Now.AddHours(-5), Now.AddHours(-3)); // 120 min deficit
        var redemption = Worked(
            RedemptionBlock(missed.BlockPresetId, Now.AddHours(-2), Now.AddHours(-1), missed.Id, 60),
            Min(0), Min(20));
        ContractHas(missed, redemption);

        // 120 raw deficit - min(60 planned, 20 actually credited) = 100
        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(100);
    }

    [Fact]
    public async Task HandleAsync_CoveringRedemptionBlockStillRunning_OptimisticFullCredit()
    {
        // A covering block still in flight isn't final (Rule 18): its claim is trusted until
        // its session ends, even though only 20 minutes are credited so far.
        var missed = CommittedBlock(Now.AddHours(-5), Now.AddHours(-3)); // 120 min deficit
        var redemption = Worked(
            RedemptionBlock(missed.BlockPresetId, Now.AddMinutes(-20), Now.AddMinutes(40), missed.Id, 60),
            Min(0), endedAfterStart: null);
        ContractHas(missed, redemption);

        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(60);
    }

    [Fact]
    public async Task HandleAsync_CoverageWithinFloatingPointTolerance_TreatedAsFullyCovered()
    {
        // Reproduces a live bug: a redemption block's MinutesCovered round-trips through a
        // tick-quantized timestamp and loses a sliver of precision. A redemption that
        // visibly, functionally fully covers its target must not leave a microscopic phantom
        // deficit that keeps the Cover Deficit button alive forever.
        var missed = CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var redemption = RedemptionBlock(missed.BlockPresetId, Now.AddHours(1), Now.AddHours(3), missed.Id, 120 - 1.7e-9);
        ContractHas(missed, redemption);

        (await Handle()).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DeficitOfExactlyOneMinute_Surfaces()
    {
        // Gap 44's 1-minute write-off is gone — every whole-minute shortfall is a real,
        // visible deficit, recoverable through Cover Deficit down to the last minute.
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(0), Min(119)));

        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_EndedSecondsShortOfAMinute_DeficitIsWholeMinutes()
    {
        // Ended 1 min 37 s short: credit rounds down to whole minutes (118), so the deficit is
        // 2 whole minutes — never a fraction, and never forgiven.
        ContractHas(Worked(CommittedBlock(Now.AddHours(-3), Now.AddHours(-1)), Min(0), TimeSpan.FromSeconds(120 * 60 - 97)));

        (await Handle()).Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_MultipleDeficits_OrderedByScheduledStart()
    {
        var later = CommittedBlock(Now.AddHours(-1).AddMinutes(-30), Now.AddHours(-1));
        var earlier = CommittedBlock(Now.AddHours(-5), Now.AddHours(-4));
        ContractHas(later, earlier);

        var result = await Handle();

        result.Should().HaveCount(2);
        result[0].FocusBlockId.Should().Be(earlier.Id);
        result[1].FocusBlockId.Should().Be(later.Id);
    }
}
