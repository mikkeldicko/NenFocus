using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetSessionHistory;

public sealed class GetSessionHistoryQueryHandler
    : IQueryHandler<GetSessionHistoryQuery, IReadOnlyList<SessionSummaryDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public GetSessionHistoryQueryHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _sessions = sessions;
        _presets = presets;
        _clock = clock;
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
            var elapsed = s.GetElapsedTime(s.EndedAt ?? _clock.Now);
            return new SessionSummaryDto
            {
                Id = s.Id,
                Goal = s.Goal.Value,
                PresetName = presetMap.TryGetValue(s.BlockPresetId, out var name)
                    ? name : "Unknown",
                PlannedDurationMinutes = (int)s.Duration.Value.TotalMinutes,
                ActualDurationMinutes = elapsed.HasValue
                    ? (int)elapsed.Value.TotalMinutes : 0,
                StartedAt = s.StartedAt ?? default,
                EndedAt = s.EndedAt,
                Status = s.Status,
                BreaksTaken = s.BreaksTaken
            };
        }).ToList();
    }
}
