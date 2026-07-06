using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetActiveSession;

public sealed class GetActiveSessionQueryHandler
    : IQueryHandler<GetActiveSessionQuery, SessionDto?>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;

    public GetActiveSessionQueryHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets)
    {
        _sessions = sessions;
        _presets = presets;
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
            StartedAt = session.StartedAt ?? DateTime.UtcNow,
            EndAt = session.EndAt ?? DateTime.UtcNow,
            EndedAt = session.EndedAt,
            BreakSettings = session.BreakSettings is null ? null : new BreakSettingsDto
            {
                IntervalMinutes = session.BreakSettings.IntervalMinutes,
                BreakDurationMinutes = session.BreakSettings.BreakLengthMinutes
            }
        };
    }
}
