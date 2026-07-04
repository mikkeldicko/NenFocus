using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed class GetFocusBlockRitualContextQueryHandler
    : IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;

    public GetFocusBlockRitualContextQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _profiles = profiles;
    }

    public async Task<FocusBlockRitualContext?> HandleAsync(GetFocusBlockRitualContextQuery query, CancellationToken ct)
    {
        var block = await _blocks.GetByIdAsync(query.FocusBlockId, ct) as FocusBlock;
        if (block is null) return null;

        var profile = await _profiles.GetByIdAsync(block.BlockProfileId, ct);
        if (profile is null) return null;

        return new FocusBlockRitualContext
        {
            Profile = BlockProfileDto.FromEntity(profile),
            Schedule = new FocusBlockSchedule { Start = block.Start, End = block.End }
        };
    }
}
