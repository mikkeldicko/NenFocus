using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;

public sealed class CheckFocusBlockBatchOverlapQueryHandler : IQueryHandler<CheckFocusBlockBatchOverlapQuery, bool>
{
    private readonly IPlannedBlockRepository _blocks;

    public CheckFocusBlockBatchOverlapQueryHandler(IPlannedBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<bool> HandleAsync(CheckFocusBlockBatchOverlapQuery query, CancellationToken ct = default)
    {
        foreach (var focusBlock in query.FocusBlocks)
        {
            var overlapping = await _blocks.GetOverlappingAsync(focusBlock.Start, focusBlock.End, ritualOnly: true, cancellationToken: ct);
            if (overlapping.Any(b => query.ExcludeContractId is null || b.ContractId != query.ExcludeContractId))
                return true;
        }

        return false;
    }
}
