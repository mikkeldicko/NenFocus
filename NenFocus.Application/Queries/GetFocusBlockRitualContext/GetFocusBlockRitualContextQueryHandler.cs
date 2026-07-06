using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed class GetFocusBlockRitualContextQueryHandler
    : IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;

    public GetFocusBlockRitualContextQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets)
    {
        _blocks = blocks;
        _presets = presets;
    }

    public async Task<FocusBlockRitualContext?> HandleAsync(GetFocusBlockRitualContextQuery query, CancellationToken ct)
    {
        var block = await _blocks.GetByIdAsync(query.FocusBlockId, ct) as FocusBlock;
        if (block is null) return null;

        var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
        if (preset is null) return null;

        return new FocusBlockRitualContext
        {
            Preset = BlockPresetDto.FromEntity(preset),
            Schedule = new FocusBlockSchedule { Start = block.Start, End = block.End }
        };
    }
}
