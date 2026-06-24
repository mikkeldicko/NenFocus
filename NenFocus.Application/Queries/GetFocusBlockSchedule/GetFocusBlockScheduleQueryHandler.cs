using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetFocusBlockSchedule;

public sealed class GetFocusBlockScheduleQueryHandler
    : IQueryHandler<GetFocusBlockScheduleQuery, FocusBlockSchedule?>
{
    private readonly IPlannedBlockRepository _blocks;

    public GetFocusBlockScheduleQueryHandler(IPlannedBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<FocusBlockSchedule?> HandleAsync(GetFocusBlockScheduleQuery query, CancellationToken ct)
    {
        var block = await _blocks.GetByIdAsync(query.FocusBlockId, ct) as FocusBlock;
        if (block is null) return null;

        return new FocusBlockSchedule { Start = block.Start, End = block.End };
    }
}
