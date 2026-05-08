using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetSessionHistory;

public sealed record GetSessionHistoryQuery
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }

    /// <summary>Optional filter by profile. Returns all profiles if null.</summary>
    public Guid? ProfileId { get; init; }
}

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

        // Load all profiles in one call to avoid N+1
        var allProfiles = await _profiles.GetAllAsync(cancellationToken);
        var profileMap = allProfiles.ToDictionary(p => p.Id, p => p.Name);

        return sessions.Select(s =>
        {
            var elapsed = s.GetElapsedTime();
            return new SessionSummaryDto
            {
                Id = s.Id,
                Goal = s.Goal.Value,
                ProfileName = profileMap.TryGetValue(s.BlockProfileId, out var name)
                    ? name : "Unknown",
                PlannedDurationMinutes = (int)s.Duration.Value.TotalMinutes,
                ActualDurationMinutes = elapsed.HasValue
                    ? (int)elapsed.Value.TotalMinutes : 0,
                StartedAt = s.StartedAt ?? s.CreatedAt,
                EndedAt = s.EndedAt,
                Completed = s.Status == Domain.Enums.SessionStatus.Completed,
                BreaksTaken = s.BreaksTaken
            };
        }).ToList();
    }
}
