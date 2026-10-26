using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetActiveSession;

public sealed class GetActiveSessionQueryHandler
    : IQueryHandler<GetActiveSessionQuery, SessionDto?>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;

    public GetActiveSessionQueryHandler(
        ISessionRepository sessions,
        IBlockRepository blocks,
        IBlockPresetRepository presets)
    {
        _sessions = sessions;
        _blocks = blocks;
        _presets = presets;
    }

    public async Task<SessionDto?> HandleAsync(
        GetActiveSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetActiveAsync(cancellationToken);
        if (session is null) return null;

        // The session's preset lives on its block (Gap 114, D1) — the session stores no copy.
        var block = await _blocks.GetByIdAsync(session.OriginatingBlockId, cancellationToken);
        var preset = block is null ? null : await _presets.GetByIdAsync(block.BlockPresetId, cancellationToken);

        // The one builder of SessionDto: IActiveSessionState refreshes from this query at
        // startup and after a session starts or ends, so nothing else maps a running session
        // (Gap 92, A3 — Gap 89 F1, F3).
        return new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            PresetName = preset?.Name ?? "Unknown",
            Status = session.Status,
            OriginatingBlockId = session.OriginatingBlockId,
            Timeline = session.Timeline,
            BreakPresetName = session.BreakPresetName
        };
    }
}
