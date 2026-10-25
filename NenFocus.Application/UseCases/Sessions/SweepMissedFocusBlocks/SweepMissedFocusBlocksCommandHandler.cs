using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed class SweepMissedFocusBlocksCommandHandler
    : ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> _markMissed;

    public SweepMissedFocusBlocksCommandHandler(
        IPlannedBlockRepository blocks,
        ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> markMissed)
    {
        _blocks = blocks;
        _markMissed = markMissed;
    }

    public async Task<SweepMissedFocusBlocksResult> HandleAsync(
        SweepMissedFocusBlocksCommand command,
        CancellationToken ct = default)
    {
        var unresolved = await _blocks.GetUnresolvedFocusBlocksAsync(command.AsOf, ct);

        var missedSessions = new List<PostSessionDto>();
        foreach (var block in unresolved.OrderBy(b => b.Scheduled))
        {
            var result = await _markMissed.HandleAsync(
                new MarkFocusBlockMissedCommand { FocusBlockId = block.Id }, ct);

            if (result.Success && result.PostSession is not null)
                missedSessions.Add(result.PostSession);
        }

        return new SweepMissedFocusBlocksResult { MissedSessions = missedSessions };
    }
}
