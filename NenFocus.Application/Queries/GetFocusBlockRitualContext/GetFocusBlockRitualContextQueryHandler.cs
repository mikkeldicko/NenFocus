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
    private readonly ISystemClock _clock;

    public GetFocusBlockRitualContextQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _clock = clock;
    }

    public async Task<FocusBlockRitualContext?> HandleAsync(GetFocusBlockRitualContextQuery query, CancellationToken ct)
    {
        var block = await _blocks.GetByIdAsync(query.FocusBlockId, ct) as FocusBlock;
        if (block is null) return null;

        var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
        if (preset is null) return null;

        // Reuses the same grace period FocusBlockOutcomeClassifier already applies to a
        // late ritual completion — one definition of "late enough to matter," not a
        // second one invented for this modal.
        var lateBy = _clock.UtcNow - block.Start;

        return new FocusBlockRitualContext
        {
            Preset = BlockPresetDto.FromEntity(preset),
            Schedule = new FocusBlockSchedule { Start = block.Start, End = block.End },
            LateBy = lateBy,
            IsLate = lateBy.TotalMinutes > FocusBlockOutcomeClassifier.GracePeriodMinutes
        };
    }
}
