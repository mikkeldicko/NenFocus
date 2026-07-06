using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetSessionHistory;

public sealed class GetSessionHistoryQueryHandler
    : IQueryHandler<GetSessionHistoryQuery, IReadOnlyList<SessionSummaryDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;

    public GetSessionHistoryQueryHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets)
    {
        _sessions = sessions;
        _presets = presets;
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> HandleAsync(
        GetSessionHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _sessions.GetHistoryAsync(
            query.From,
            query.To,
            query.PresetId,
            cancellationToken);

        var allPresets = await _presets.GetAllAsync(cancellationToken);
        var presetMap = allPresets.ToDictionary(p => p.Id, p => p.Name);

        return sessions.Select(s =>
        {
            var elapsed = s.GetElapsedTime(s.EndedAt ?? DateTime.UtcNow);
            return new SessionSummaryDto
            {
                Id = s.Id,
                Goal = s.Goal.Value,
                PresetName = presetMap.TryGetValue(s.BlockPresetId, out var name)
                    ? name : "Unknown",
                PlannedDurationMinutes = (int)s.Duration.Value.TotalMinutes,
                ActualDurationMinutes = elapsed.HasValue
                    ? (int)elapsed.Value.TotalMinutes : 0,
                StartedAt = s.StartedAt.GetValueOrDefault(),
                EndedAt = s.EndedAt,
                Status = s.Status,
                BreaksTaken = s.BreaksTaken
            };
        }).ToList();
    }
}
