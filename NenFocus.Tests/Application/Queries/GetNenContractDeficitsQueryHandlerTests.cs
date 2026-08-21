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
    public async Task HandleAsync_MissedBlockNoSession_FullDeficit()
    {
        var block = MissedBlock(Now.AddHours(-3), Now.AddHours(-1)); // 2-hour block
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.Classify(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);

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
        // If the fix regresses, the handler starts calling Classify for the redemption
        // block too — stubbing StartedLate here means a regression shows up as a second,
        // real deficit entry in the result instead of silently passing either way.
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.StartedLate);

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

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().ContainSingle().Subject.RemainingDeficitMinutes.Should().Be(70); // 120 - 50
    }

    [Fact]
    public async Task HandleAsync_MultipleDeficits_OrderedByScheduledStart()
    {
        var later = MissedBlock(Now.AddHours(-1).AddMinutes(-30), Now.AddHours(-1));
        var earlier = MissedBlock(Now.AddHours(-5), Now.AddHours(-4));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[later, earlier]);
        _classifier.Classify(later.Start, later.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);
        _classifier.Classify(earlier.Start, earlier.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now).Returns(FocusBlockOutcome.Missed);

        var result = await CreateHandler().HandleAsync(new GetNenContractDeficitsQuery(ContractId), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].FocusBlockId.Should().Be(earlier.Id);
        result[1].FocusBlockId.Should().Be(later.Id);
    }
}
