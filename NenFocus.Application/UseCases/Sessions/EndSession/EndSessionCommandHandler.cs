using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed class EndSessionCommandHandler
    : ICommandHandler<EndSessionCommand, EndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly IBlockedAttemptTracker _attemptTracker;
    private readonly ISystemClock _clock;

    public EndSessionCommandHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        IPlannedBlockRepository plannedBlocks,
        IBlockedAttemptTracker attemptTracker,
        ISystemClock clock)
    {
        _sessions = sessions;
        _presets = presets;
        _plannedBlocks = plannedBlocks;
        _attemptTracker = attemptTracker;
        _clock = clock;
    }

    public async Task<EndSessionResult> HandleAsync(
        EndSessionCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null)
            return EndSessionResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Active
            && session.Status != SessionStatus.OnBreak)
            return EndSessionResult.Fail("Session is not currently active.");

        var attempts = await _attemptTracker.GetAttemptsForSessionAsync(session.Id);

        var appAttempts = attempts
            .Where(a => a.AttemptType == BlockedAttemptType.App)
            .GroupBy(a => a.ResourceName)
            .ToDictionary(g => g.Key, g => g.Count());

        var websiteAttempts = attempts
            .Where(a => a.AttemptType == BlockedAttemptType.Website)
            .GroupBy(a => a.ResourceName)
            .ToDictionary(g => g.Key, g => g.Count());

        var endedAt = _clock.UtcNow;
        session.Complete(endedAt);

        await _sessions.SaveAsync(session, ct);

        if (session.OriginatingBlockId.HasValue)
        {
            var block = await _plannedBlocks.GetByIdAsync(session.OriginatingBlockId.Value, ct);
            if (block is FocusBlock focusBlock && endedAt < focusBlock.End)
            {
                focusBlock.MarkSessionEnded(endedAt);
                await _plannedBlocks.SaveAsync(focusBlock, ct);
            }
        }

        var presetName = string.Empty;
        if (session.BlockPresetId != Guid.Empty)
        {
            var preset = await _presets.GetByIdAsync(session.BlockPresetId, ct);
            presetName = preset?.Name ?? string.Empty;
        }

        var postSession = new PostSessionDto
        {
            SessionId = session.Id,
            Status = session.Status,
            Goal = session.Goal.Value,
            PresetName = presetName,
            StartedAt = session.StartedAt!.Value,
            EndedAt = session.EndedAt!.Value,
            PlannedDurationMinutes = (int)session.Duration.Value.TotalMinutes,
            ActualDurationMinutes = (int)(session.GetElapsedTime(session.EndedAt.Value)?.TotalMinutes ?? 0),
            BreaksTaken = session.BreaksTaken,
            BlockedAppAttempts = appAttempts,
            BlockedWebsiteAttempts = websiteAttempts,
            TotalBlockedAttempts = attempts.Count
        };

        return EndSessionResult.Ok(postSession);
    }
}