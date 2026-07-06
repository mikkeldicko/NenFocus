using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed class SweepMissedFocusBlocksCommandHandler
    : ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> _markMissed;
    private readonly ISystemClock _clock;

    public SweepMissedFocusBlocksCommandHandler(
        IPlannedBlockRepository blocks,
        ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> markMissed,
        ISystemClock clock)
    {
        _blocks = blocks;
        _markMissed = markMissed;
        _clock = clock;
    }

    public async Task<SweepMissedFocusBlocksResult> HandleAsync(
        SweepMissedFocusBlocksCommand command,
        CancellationToken ct = default)
    {
        var unresolved = await _blocks.GetUnresolvedFocusBlocksAsync(_clock.UtcNow, ct);

        var missedSessions = new List<PostSessionDto>();
        foreach (var block in unresolved.OrderBy(b => b.Start))
        {
            var result = await _markMissed.HandleAsync(
                new MarkFocusBlockMissedCommand { FocusBlockId = block.Id }, ct);

            if (result.Success && result.PostSession is not null)
                missedSessions.Add(result.PostSession);
        }

        return new SweepMissedFocusBlocksResult { MissedSessions = missedSessions };
    }
}
