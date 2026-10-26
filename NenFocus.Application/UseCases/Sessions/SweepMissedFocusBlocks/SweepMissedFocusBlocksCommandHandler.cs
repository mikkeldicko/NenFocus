using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed class SweepMissedFocusBlocksCommandHandler
    : ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult>
{
    private readonly IBlockRepository _blocks;

    public SweepMissedFocusBlocksCommandHandler(IBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<SweepMissedFocusBlocksResult> HandleAsync(
        SweepMissedFocusBlocksCommand command,
        CancellationToken ct = default)
    {
        // A miss is the block's own outcome now (Gap 114, D5): the sweep only stamps each
        // unresolved block, and its own guard refuses one already frozen. The Post-Session recap
        // for a miss reads the block (Gap 114 phase 3), so nothing is relayed from here.
        var unresolved = await _blocks.GetUnresolvedFocusBlocksAsync(command.AsOf, ct);

        foreach (var block in unresolved.OrderBy(b => b.Scheduled))
            block.MarkMissed(command.AsOf);

        return new SweepMissedFocusBlocksResult();
    }
}
