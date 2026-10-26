using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class SweepMissedFocusBlocksCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();

    private SweepMissedFocusBlocksCommandHandler CreateHandler() => new(_blocks);

    [Fact]
    public async Task HandleAsync_NoUnresolvedBlocks_StampsNothing()
    {
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[]);

        await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(Now) });

        // The sweep only stamps what is unresolved — an empty list means no block changed.
        await _blocks.Received(1).GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnresolvedBlocks_MarksEachMissed()
    {
        var earlier = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-3), Now.AddHours(-2)));
        var later = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-2), Now.AddHours(-1)));
        _blocks.GetUnresolvedFocusBlocksAsync(At.Utc(Now), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlock>)[later, earlier]);

        await CreateHandler().HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = At.Utc(Now) });

        // A miss is the block's own outcome now (Gap 114, D5) — the sweep freezes each block.
        earlier.Outcome(At.Utc(Now)).Should().Be(FocusBlockOutcome.Missed);
        later.Outcome(At.Utc(Now)).Should().Be(FocusBlockOutcome.Missed);
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
}
