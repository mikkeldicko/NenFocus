using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractDeficitsQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IFocusBlockOutcomeClassifier _classifier = Substitute.For<IFocusBlockOutcomeClassifier>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetNenContractDeficitsQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private GetNenContractDeficitsQueryHandler CreateHandler() => new(_blocks, _sessions, _classifier, _clock);

    private static FocusBlock MissedBlock(DateTime start, DateTime end, Guid? presetId = null) =>
        FocusBlock.Create(Guid.NewGuid(), presetId ?? Guid.NewGuid(), start, end, contractId: ContractId);

    /// <summary>A genuinely finished Missed session — the state MarkFocusBlockMissedCommandHandler
    /// produces in real usage. Deficits now require the block's session to have actually
    /// ended (HasEnded) before they're trusted (see the "still active" test below).</summary>
    private static Session EndedMissedSession(FocusBlock block)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("(missed — no goal set)"),
            new SessionDuration(block.GetDuration()), block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Miss(block.Start, block.End);
        return session;
    }

    [Fact]
    public async Task HandleAsync_NoFocusBlocks_ReturnsEmpty()
    {
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CompletedOnTimeBlock_Excluded()
    {
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.CompletedOnTime);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_OpenBlock_Excluded()
    {
        var block = MissedBlock(Now.AddHours(-1), Now.AddHours(1));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns((FocusBlockOutcome?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_StartedLateSessionStillActive_ExcludedFromDeficit()
    {
        // The core case this gate exists for: a 1-hour block started 10 minutes late is
        // still running, with no idea yet whether it'll finish on schedule or end early —
        // its final credited minutes aren't knowable, so it must not surface as a deficit
        // (or a Cover Deficit suggestion) while still in flight.
        var block = MissedBlock(Now.AddMinutes(-10), Now.AddMinutes(50)); // 1-hour block, started 10 min late
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.StartedLate);

        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(block.Start.AddMinutes(10), block.End); // still running — no Complete() call, so HasEnded is false
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_SessionEndedButPostSessionNotAcknowledged_StillCountsTowardDeficit()
    {
        // Acknowledging the Post-Session screen is a separate, unrelated fact (it exists for
        // crash-recovery replay, not deficit finality) — once the session has actually ended,
        // the deficit is real and visible regardless of whether the user has dismissed that
        // screen yet.
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block, 120 min
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.EndedEarly);

        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(block.Start);
        session.Complete(block.Start.AddMinutes(45)); // ended — PostSessionAcknowledgedAt deliberately left null
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);
        _classifier.CreditedFocusMinutes(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>()).Returns(45);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(75);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockEnded_FullDeficit()
    {
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(block));

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        var dto = result.Should().ContainSingle().Subject;
        dto.FocusBlockId.Should().Be(block.Id);
        dto.Outcome.Should().Be(FocusBlockOutcome.Missed);
        dto.RemainingDeficitMinutes.Should().Be(120);
        dto.PlannedMinutes.Should().Be(120);
        dto.CompletedMinutes.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_StartedLateBlockWithPartialSession_DeficitIsScheduledMinusActual()
    {
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block, 120 min
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.StartedLate);

        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(90)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(block.Start.AddMinutes(20), block.End);
        session.Complete(block.Start.AddMinutes(20 + 90)); // 90 minutes actually completed
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);
        _classifier.CreditedFocusMinutes(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>()).Returns(90);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        var dto = result.Should().ContainSingle().Subject;
        dto.RemainingDeficitMinutes.Should().Be(30); // 120 - 90
        dto.PlannedMinutes.Should().Be(120);
        dto.CompletedMinutes.Should().Be(90);
    }

    [Fact]
    public async Task HandleAsync_EndedEarlyBlock_DeficitIsScheduledMinusActual()
    {
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block, 120 min
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.EndedEarly);

        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(block.Start); // started right on time
        session.Complete(block.Start.AddMinutes(45)); // but quit after 45 of the 120 scheduled minutes
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);
        _classifier.CreditedFocusMinutes(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>()).Returns(45);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        var dto = result.Should().ContainSingle().Subject;
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.RemainingDeficitMinutes.Should().Be(75); // 120 - 45 — the early-end gap is no longer silently lost
        dto.PlannedMinutes.Should().Be(120);
        dto.CompletedMinutes.Should().Be(45);
    }

    [Fact]
    public async Task HandleAsync_StartedLateSessionRanPastBlocksScheduledEnd_DeficitUsesCreditedNotRawElapsed()
    {
        // Block scheduled for 120 minutes; ritual completed 40 min late. The session then
        // ran a full 150 minutes from that late start, extending 70 minutes past the
        // block's own scheduled end. Raw elapsed (150) exceeds scheduled (120) — the bug
        // this reproduces let that raw excess silently zero out a real remaining deficit.
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block, 120 min
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.StartedLate);

        var ritualCompletedAt = block.Start.AddMinutes(40);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(150)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(ritualCompletedAt, block.End);
        session.Complete(ritualCompletedAt.AddMinutes(150)); // 70 minutes past block.End
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);

        // Credited is capped at scheduledEnd - completedAt (120 - 40 = 80), same rule
        // Daily Floor crediting already applies — the deficit query must reuse it, not
        // re-derive its own number from raw elapsed time.
        _classifier.CreditedFocusMinutes(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>())
            .Returns(80);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        var dto = result.Should().ContainSingle().Subject;
        dto.RemainingDeficitMinutes.Should().Be(40); // 120 - 80
        dto.CompletedMinutes.Should().Be(80);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockFullyCoveredByRedemption_Excluded()
    {
        var missed = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(1), Now.AddHours(3),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 120)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns((FocusBlockOutcome?)null);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_RedemptionBlockItselfStartedLate_DoesNotAppearAsItsOwnDeficit()
    {
        // Reproduces the reported bug: a redemption block that itself runs a few minutes
        // past its own grace period (e.g. the Ritual took a moment) must not become a
        // second, uncoverable deficit — it fully covered the block it exists to redeem,
        // and that's the only fact that matters.
        var missed = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(-2), Now,
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 120)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));
        // If the fix regresses, the handler starts calling Classify for the redemption
        // block too — stubbing StartedLate here means a regression shows up as a second,
        // real deficit entry in the result instead of silently passing either way.
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.StartedLate);
        // Starting a few minutes late doesn't mean it delivered nothing — it still ran its
        // full committed 120 minutes, which is what Rule 13's coverage cap should honor.
        var redemptionSession = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            presetId, SessionOrigin.Planned, originatingBlockId: redemption.Id);
        redemptionSession.Start(redemption.Start, redemption.End);
        redemptionSession.Complete(redemption.End);
        _sessions.GetByOriginatingBlockIdAsync(redemption.Id, Arg.Any<CancellationToken>()).Returns(redemptionSession);
        _classifier.CreditedFocusMinutes(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>())
            .Returns(120);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ExcludeCoverageFromBlockId_ThatBlocksOwnEntryIsIgnored()
    {
        // Reproduces the live bug: recomputing a redemption block's coverage on edit must
        // not see the block's own stale, pre-edit RedemptionEntry as coverage from someone
        // else — without the exclusion, remainingDeficit collapses toward zero (floating-
        // point noise, not a clean 0) instead of the true 120-minute deficit.
        var missed = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var editingBlockId = Guid.NewGuid();
        var redemption = FocusBlock.Create(editingBlockId, presetId, Now.AddHours(1), Now.AddHours(3),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 120)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));

        var result = await CreateHandler().HandleAsync(
            new GetNenContractDeficitsQuery(ContractId, ExcludeCoverageFromBlockId: editingBlockId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(120);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockPartiallyCoveredByRedemption_RemainingDeficitReduced()
    {
        var missed = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(1), Now.AddHours(2),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 50)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns((FocusBlockOutcome?)null);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(70); // 120 - 50
    }

    [Fact]
    public async Task HandleAsync_CoveringRedemptionBlockItselfMissed_OriginalDeficitReopens()
    {
        // Rule 13's second half: a redemption block that never actually happened must not
        // keep "paying off" the deficit it was scheduled to cover — the original block's
        // deficit reopens in full.
        var missed = MissedBlock(Now.AddHours(-5), Now.AddHours(-3)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(-2), Now.AddHours(-1),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 60)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(redemption.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(redemption));

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(120); // full deficit, not 60
    }

    [Fact]
    public async Task HandleAsync_CoveringRedemptionBlockEndedEarly_CoverageCappedAtActualCredit()
    {
        // A redemption block scheduled to cover 60 minutes but that itself only delivered
        // 20 real credited minutes (ended early) must not get credit for the full 60 it
        // was planned for — only its own actual, live outcome counts.
        var missed = MissedBlock(Now.AddHours(-5), Now.AddHours(-3)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(-2), Now.AddHours(-1),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 60)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.EndedEarly);
        var redemptionSession = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            presetId, SessionOrigin.Planned, originatingBlockId: redemption.Id);
        redemptionSession.Start(redemption.Start, redemption.End);
        redemptionSession.Complete(redemption.Start.AddMinutes(20));
        _sessions.GetByOriginatingBlockIdAsync(redemption.Id, Arg.Any<CancellationToken>()).Returns(redemptionSession);
        _classifier.CreditedFocusMinutes(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>())
            .Returns(20);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        // 120 raw deficit - min(60 planned, 20 actually credited) = 100
        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(100);
    }

    [Fact]
    public async Task HandleAsync_CoveringRedemptionBlockStillPending_OptimisticFullCredit()
    {
        // A redemption block scheduled ahead but not yet started (no verdict yet) should
        // still count as covering — a "cover deficit" tile shouldn't reopen the original
        // deficit just because it hasn't happened yet.
        var missed = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(1), Now.AddHours(3),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 120)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns((FocusBlockOutcome?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CoverageWithinFloatingPointTolerance_TreatedAsFullyCovered()
    {
        // Reproduces a live bug: a redemption block's duration is sized to a deficit
        // expressed as a double, round-tripped through a DateTime End (quantized to 100ns
        // ticks), then read back out as TotalMinutes for its MinutesCovered — losing a
        // sliver of precision every time. A redemption that visibly, functionally fully
        // covers its target must not leave a microscopic phantom deficit that keeps the
        // Cover Deficit button alive forever.
        var missed = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 120 min deficit
        var presetId = missed.BlockPresetId;
        // 1.7e-9 minutes short of the real 120-minute deficit — exactly the kind of residual
        // the double -> DateTime -> double round-trip through tick quantization produces.
        var redemption = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddHours(1), Now.AddHours(3),
            contractId: ContractId, redemptionEntries: [new RedemptionEntry(missed.Id, 120 - 1.7e-9)]);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, redemption]);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(missed.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(missed));
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns((FocusBlockOutcome?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DeficitOfExactlyOneMinute_Surfaces()
    {
        // Gap 44's 1-minute write-off is gone — every whole-minute shortfall is a real,
        // visible deficit now, recoverable through Cover Deficit down to the last minute.
        // A clean 1-minute deficit is the smallest one that can exist once credited minutes
        // are floored at the source, and it must surface, not be silently forgiven.
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block, 120 min
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.EndedEarly);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(block.Start);
        session.Complete(block.End.AddMinutes(-1)); // exactly 1 minute short
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);
        _classifier.CreditedFocusMinutes(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>()).Returns(119);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_DeficitJustOverWriteOffThreshold_StillSurfaces()
    {
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block, 120 min
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.EndedEarly);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(block.Start);
        session.Complete(block.End.AddSeconds(-97)); // 1 min 37 sec short
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);
        _classifier.CreditedFocusMinutes(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>())
            .Returns(120 - 97 / 60.0);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().BeApproximately(97 / 60.0, 1e-9);
    }

    [Fact]
    public async Task HandleAsync_MultipleDeficits_OrderedByScheduledStart()
    {
        var later = MissedBlock(Now.AddHours(-1).AddMinutes(-30), Now.AddHours(-1));
        var earlier = MissedBlock(Now.AddHours(-5), Now.AddHours(-4));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[later, earlier]);
        _classifier.Classify(later.Start, later.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(later.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(later));
        _classifier.Classify(earlier.Start, earlier.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _sessions.GetByOriginatingBlockIdAsync(earlier.Id, Arg.Any<CancellationToken>()).Returns(EndedMissedSession(earlier));

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].FocusBlockId.Should().Be(earlier.Id);
        result[1].FocusBlockId.Should().Be(later.Id);
    }
}
