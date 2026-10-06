using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed class GetFocusBlockRitualContextQueryHandler
    : IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;

    public GetFocusBlockRitualContextQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISessionRepository sessions,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<FocusBlockRitualContext?> HandleAsync(GetFocusBlockRitualContextQuery query, CancellationToken ct)
    {
        var block = await _blocks.GetByIdAsync(query.FocusBlockId, ct) as FocusBlock;
        if (block is null) return null;

        var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
        if (preset is null) return null;

        // Asks the block's own IsLateAt — one definition of "late enough
        // to matter," not a second one invented for this modal — against a live "if the
        // ritual completed right now" check, since the ritual hasn't happened yet.
        var now = _clock.Now;

        return new FocusBlockRitualContext
        {
            Preset = BlockPresetDto.FromEntity(preset),
            Schedule = new FocusBlockSchedule { Slot = block.Scheduled },
            LateBy = -block.Scheduled.StartsIn(now),
            IsLate = block.IsLateAt(now),
            AlreadyHasSession = await _sessions.ExistsForOriginatingBlockAsync(block.Id, ct)
        };
    }
}
