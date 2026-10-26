using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetSessionSnapshotForBlock;

public sealed class GetSessionSnapshotForBlockQueryHandler
    : IQueryHandler<GetSessionSnapshotForBlockQuery, SessionSnapshotDto?>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockedAttemptRepository _attempts;
    private readonly IBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public GetSessionSnapshotForBlockQueryHandler(
        ISessionRepository sessions,
        IBlockedAttemptRepository attempts,
        IBlockRepository blocks,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _sessions = sessions;
        _attempts = attempts;
        _blocks = blocks;
        _presets = presets;
        _clock = clock;
    }

    public async Task<SessionSnapshotDto?> HandleAsync(
        GetSessionSnapshotForBlockQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByOriginatingBlockIdAsync(query.BlockId, cancellationToken);
        if (session is null) return null;

        var attempts = await _attempts.GetForSessionAsync(session.Id, cancellationToken);

        var block = await _blocks.GetByIdAsync(query.BlockId, cancellationToken) as FocusBlock;
        var redemptionCoverage = new List<RedemptionCoverageDto>();
        if (block is not null)
        {
            foreach (var entry in block.Receipt.Entries)
            {
                var missedBlock = await _blocks.GetByIdAsync(entry.MissedBlockId, cancellationToken);
                var missedPreset = missedBlock is not null
                    ? await _presets.GetByIdAsync(missedBlock.BlockPresetId, cancellationToken)
                    : null;

                redemptionCoverage.Add(new RedemptionCoverageDto
                {
                    MissedBlockId = entry.MissedBlockId,
                    MissedBlockPresetName = missedPreset?.Name ?? "Unknown",
                    MissedBlockDate = missedBlock is not null
                        ? DateOnly.FromDateTime(_clock.TimeZone.ToCalendarTimes(missedBlock.Scheduled).Start)
                        : default,
                    Covered = entry.Covered
                });
            }
        }

        return new SessionSnapshotDto
        {
            Goal = session.Goal.Value,
            Reflection = session.Reflection,
            Attempts = attempts
                .Select(a => new BlockedAttemptSnapshotDto
                {
                    ResourceName = a.ResourceName,
                    AttemptType = a.AttemptType,
                    OccurredAt = a.OccurredAt
                })
                .ToList(),
            RedemptionCoverage = redemptionCoverage
        };
    }
}
