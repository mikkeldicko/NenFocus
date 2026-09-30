using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Services;

/// <summary>
/// Turns each block's own "which of my markers did the clock just pass" answer into a
/// notification fact — no rule of its own. The fetch is deliberately loose (anything that
/// could have crossed); PlannedBlock.MarkersPassedBetween is the final word, as IsActive is
/// for PolicyEngine.
/// </summary>
public sealed class BlockMarkerNotifier : IBlockMarkerNotifier
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INotifier _notifier;

    public BlockMarkerNotifier(IPlannedBlockRepository blocks, IBlockPresetRepository presets, INotifier notifier)
    {
        _blocks = blocks;
        _presets = presets;
        _notifier = notifier;
    }

    public async Task NotifyPassedAsync(Moment from, Moment to, CancellationToken ct = default)
    {
        if (to <= from) return;

        var candidates = await _blocks.GetForRangeAsync(new TimeSlot(from, to + PlannedBlock.UpcomingWarningLead), ct);

        var crossed = candidates
            .SelectMany(block => block.MarkersPassedBetween(from, to).Select(t => (Block: block, Marker: t)))
            .OrderBy(x => x.Marker.At)
            // Back-to-back blocks: the old one ends before the new one starts.
            .ThenBy(x => x.Marker.Kind == BlockMarkerKind.Ended ? 0 : 1);

        var presetNames = new Dictionary<Guid, string>();

        foreach (var (block, marker) in crossed)
        {
            if (!presetNames.TryGetValue(block.BlockPresetId, out var presetName))
            {
                var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
                presetNames[block.BlockPresetId] = presetName = preset?.Name ?? "";
            }

            var name = block.DisplayName(presetName);
            NenNotification fact = marker.Kind switch
            {
                BlockMarkerKind.StartingSoon => new BlockStartingSoon(
                    block.PresetType, name, (int)Math.Ceiling(block.Scheduled.StartsIn(to).TotalMinutes)),
                BlockMarkerKind.Started => new BlockStarted(block.PresetType, name),
                _ => new BlockEnded(block.PresetType, name)
            };

            await _notifier.NotifyAsync(fact, ct);
        }
    }
}
