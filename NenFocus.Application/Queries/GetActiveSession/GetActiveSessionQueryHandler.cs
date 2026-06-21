using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetActiveSession;

public sealed class GetActiveSessionQueryHandler
    : IQueryHandler<GetActiveSessionQuery, SessionDto?>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockProfileRepository _profiles;

    public GetActiveSessionQueryHandler(
        ISessionRepository sessions,
        IBlockProfileRepository profiles)
    {
        _sessions = sessions;
        _profiles = profiles;
    }

    public async Task<SessionDto?> HandleAsync(
        GetActiveSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetActiveAsync(cancellationToken);
        if (session is null) return null;

        var profile = await _profiles.GetByIdAsync(session.BlockProfileId, cancellationToken);

        return new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            ProfileName = profile?.Name ?? "Unknown",
            DurationMinutes = (int)session.Duration.Value.TotalMinutes,
            Status = session.Status,
            StartedAt = session.StartedAt ?? DateTime.UtcNow,
            EndedAt = session.EndedAt,
            BreakSettings = session.BreakSettings is null ? null : new BreakSettingsDto
            {
                Enabled = true,
                IntervalMinutes = session.BreakSettings.IntervalMinutes,
                BreakDurationMinutes = session.BreakSettings.BreakLengthMinutes
            }
        };
    }
}
