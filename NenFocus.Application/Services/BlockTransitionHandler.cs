using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Services;

/// <summary>
/// Turns each block's own "which of my boundaries did the clock just cross" answer into a
/// notification fact — no rule of its own. The fetch is deliberately loose (anything that
/// could have crossed); PlannedBlock.TransitionsBetween is the final word, as IsActive is
/// for PolicyEngine.
/// </summary>
public sealed class BlockTransitionHandler : IBlockTransitionHandler
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INotifier _notifier;

    public BlockTransitionHandler(IPlannedBlockRepository blocks, IBlockPresetRepository presets, INotifier notifier)
    {
        _blocks = blocks;
        _presets = presets;
        _notifier = notifier;
    }

    public async Task HandleAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (to <= from) return;

        var candidates = await _blocks.GetForRangeAsync(from, to + PlannedBlock.UpcomingWarningLead, ct);

        var crossed = candidates
            .SelectMany(block => block.TransitionsBetween(from, to).Select(t => (Block: block, Transition: t)))
            .OrderBy(x => x.Transition.At)
            // Back-to-back blocks: the old one ends before the new one starts.
            .ThenBy(x => x.Transition.Kind == BlockTransitionKind.Ended ? 0 : 1);

        var presetNames = new Dictionary<Guid, string>();

        foreach (var (block, transition) in crossed)
        {
            if (!presetNames.TryGetValue(block.BlockPresetId, out var presetName))
            {
                var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
                presetNames[block.BlockPresetId] = presetName = preset?.Name ?? "";
            }

            var name = block.DisplayName(presetName);
            NenNotification fact = transition.Kind switch
            {
                BlockTransitionKind.StartingSoon => new BlockStartingSoon(
                    block.PresetType, name, (int)Math.Ceiling((block.Start - to).TotalMinutes)),
                BlockTransitionKind.Started => new BlockStarted(block.PresetType, name),
                _ => new BlockEnded(block.PresetType, name)
            };

            await _notifier.NotifyAsync(fact, ct);
        }
    }
}
