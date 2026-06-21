using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetSessionHistory;

public sealed class GetSessionHistoryQueryHandler
    : IQueryHandler<GetSessionHistoryQuery, IReadOnlyList<SessionSummaryDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockProfileRepository _profiles;

    public GetSessionHistoryQueryHandler(
        ISessionRepository sessions,
        IBlockProfileRepository profiles)
    {
        _sessions = sessions;
        _profiles = profiles;
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> HandleAsync(
        GetSessionHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _sessions.GetHistoryAsync(
            query.From,
            query.To,
            query.ProfileId,
            cancellationToken);

        var allProfiles = await _profiles.GetAllAsync(cancellationToken);
        var profileMap = allProfiles.ToDictionary(p => p.Id, p => p.Name);

        return sessions.Select(s =>
        {
            var elapsed = s.GetElapsedTime(s.EndedAt ?? DateTime.UtcNow);
            return new SessionSummaryDto
            {
                Id = s.Id,
                Goal = s.Goal.Value,
                ProfileName = profileMap.TryGetValue(s.BlockProfileId, out var name)
                    ? name : "Unknown",
                PlannedDurationMinutes = (int)s.Duration.Value.TotalMinutes,
                ActualDurationMinutes = elapsed.HasValue
                    ? (int)elapsed.Value.TotalMinutes : 0,
                StartedAt = s.StartedAt.GetValueOrDefault(),
                EndedAt = s.EndedAt,
                Completed = s.Status == Domain.Enums.SessionStatus.Completed,
                BreaksTaken = s.BreaksTaken
            };
        }).ToList();
    }
}
