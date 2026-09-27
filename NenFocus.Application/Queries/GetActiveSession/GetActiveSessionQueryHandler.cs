using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetActiveSession;

public sealed class GetActiveSessionQueryHandler
    : IQueryHandler<GetActiveSessionQuery, SessionDto?>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public GetActiveSessionQueryHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _sessions = sessions;
        _presets = presets;
        _clock = clock;
    }

    public async Task<SessionDto?> HandleAsync(
        GetActiveSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetActiveAsync(cancellationToken);
        if (session is null) return null;

        var preset = await _presets.GetByIdAsync(session.BlockPresetId, cancellationToken);

        return new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            PresetName = preset?.Name ?? "Unknown",
            DurationMinutes = (int)session.Duration.Value.TotalMinutes,
            Status = session.Status,
            StartedAt = session.StartedAt ?? _clock.UtcNow,
            EndAt = session.EndAt ?? _clock.UtcNow,
            EndedAt = session.EndedAt,
            Breaks = SessionDto.MapBreaks(session.BreakSchedule),
            BreakPresetName = session.BreakPresetName
        };
    }
}
