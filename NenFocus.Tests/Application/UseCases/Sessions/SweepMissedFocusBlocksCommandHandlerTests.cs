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

    private SweepMissedFocusBlocksCommandHandler CreateHandler() => new(_blocks, _markMissed);

    [Fact]
    public async Task HandleAsync_NoUnresolvedBlocks_ReturnsEmptyResult()
    {
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[]);

        var result = await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(Now) });

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

        var earlierPostSession = new PostSessionDto { SessionId = Guid.NewGuid(), Recorded = At.Slot(new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 8, 25, 0, DateTimeKind.Utc)) };
        var laterPostSession = new PostSessionDto { SessionId = Guid.NewGuid(), Recorded = At.Slot(new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 8, 25, 0, DateTimeKind.Utc)) };
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == earlier.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Ok(Guid.NewGuid(), earlierPostSession));
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == later.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Ok(Guid.NewGuid(), laterPostSession));

        var result = await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(Now) });

        result.MissedSessions.Should().Equal(earlierPostSession, laterPostSession);
    }

    [Fact]
    public async Task HandleAsync_SweepsAtTheMomentItIsGiven()
    {
        // The heartbeat passes its tick's one "now" (required since Gap 89 Slice A), so the sweep
        // and the same tick's other steps can never disagree about what "now" means (Gap 21).
        var asOf = Now.AddHours(1);
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(asOf), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[]);

        await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(asOf) });

        await _blocks.Received(1).GetUnresolvedFocusBlocksAsync(At.Utc(asOf), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MarkMissedFails_SkipsThatBlockButContinues()
    {
        var failing = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-2)));
        var succeeding = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-2), Now.AddHours(-1)));
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[failing, succeeding]);

        var postSession = new PostSessionDto { SessionId = Guid.NewGuid(), Recorded = At.Slot(new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 8, 25, 0, DateTimeKind.Utc)) };
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == failing.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Fail("boom"));
        _markMissed.HandleAsync(
                Arg.Is<MarkFocusBlockMissedCommand>(c => c.FocusBlockId == succeeding.Id),
                Arg.Any<CancellationToken>())
            .Returns(MarkFocusBlockMissedResult.Ok(Guid.NewGuid(), postSession));

        var result = await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(Now) });

        result.MissedSessions.Should().ContainSingle().Which.Should().Be(postSession);
    }
}
