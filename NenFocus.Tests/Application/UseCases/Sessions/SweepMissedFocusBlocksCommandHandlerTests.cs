using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;
using NenFocus.Domain.Entities;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class SweepMissedFocusBlocksCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> _markMissed =
        Substitute.For<ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public SweepMissedFocusBlocksCommandHandlerTests()
    {
        _clock.SetNow(Now);
    }

    private SweepMissedFocusBlocksCommandHandler CreateHandler() => new(_blocks, _markMissed, _clock);

    [Fact]
    public async Task HandleAsync_NoUnresolvedBlocks_ReturnsEmptyResult()
    {
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[]);

        var result = await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand());

        result.MissedSessions.Should().BeEmpty();
        await _markMissed.DidNotReceive().HandleAsync(Arg.Any<MarkFocusBlockMissedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnresolvedBlocks_MarksEachMissedInStartOrderAndCollectsPostSessions()
    {
        var earlier = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-2)));
        var later = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-2), Now.AddHours(-1)));
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[later, earlier]);

        var earlierPostSession = new PostSessionDto { SessionId = Guid.NewGuid() };
        var laterPostSession = new PostSessionDto { SessionId = Guid.NewGuid() };
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == earlier.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Ok(Guid.NewGuid(), earlierPostSession));
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == later.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Ok(Guid.NewGuid(), laterPostSession));

        var result = await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand());

        result.MissedSessions.Should().Equal(earlierPostSession, laterPostSession);
    }

    [Fact]
    public async Task HandleAsync_AsOfProvided_UsesItInsteadOfClock()
    {
        // ScheduledEnforcementService passes its own already-captured "now" so the sweep
        // and the same tick's policy computation can never disagree about what "now" means
        // — closes a TOCTOU race where a stale ComputeActivePolicySliceQuery "now" could
        // still see a block as active after this handler's own independent clock read had
        // already moved past its End, triggering the Ritual for a block already marked Missed.
        var asOf = Now.AddHours(1);
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(asOf), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[]);

        await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(asOf) });

        await _blocks.Received(1).GetUnresolvedFocusBlocksAsync(At.Utc(asOf), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MarkMissedFails_SkipsThatBlockButContinues()
    {
        var failing = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-2)));
        var succeeding = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-2), Now.AddHours(-1)));
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[failing, succeeding]);

        var postSession = new PostSessionDto { SessionId = Guid.NewGuid() };
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == failing.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Fail("boom"));
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == succeeding.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Ok(Guid.NewGuid(), postSession));

        var result = await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand());

        result.MissedSessions.Should().ContainSingle().Which.Should().Be(postSession);
    }
}
