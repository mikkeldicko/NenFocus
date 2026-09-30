using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed class EndSessionCommandHandler
    : ICommandHandler<EndSessionCommand, EndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly IBlockedAttemptTracker _attemptTracker;
    private readonly ISystemClock _clock;
    private readonly IFocusBlockOutcomeClassifier _classifier;

    public EndSessionCommandHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        IPlannedBlockRepository plannedBlocks,
        IBlockedAttemptTracker attemptTracker,
        ISystemClock clock,
        IFocusBlockOutcomeClassifier classifier)
    {
        _sessions = sessions;
        _presets = presets;
        _plannedBlocks = plannedBlocks;
        _attemptTracker = attemptTracker;
        _clock = clock;
        _classifier = classifier;
    }

    public async Task<EndSessionResult> HandleAsync(
        EndSessionCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null)
            return EndSessionResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Active)
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

        session.Complete(_clock.Now);
        // Capped at the planned end by Complete itself (Gap 63). Everything below reads the
        // recorded end, never "now", so an end noticed late can't leak past the plan.
        var endedAt = session.EndedAt!.Value;

        await _sessions.SaveAsync(session, ct);

        FocusBlock? originatingFocusBlock = null;
        if (session.OriginatingBlockId.HasValue)
        {
            var block = await _plannedBlocks.GetByIdAsync(session.OriginatingBlockId.Value, ct);
            if (block is FocusBlock focusBlock)
            {
                originatingFocusBlock = focusBlock;
                // Always, early or on time: the block's SessionEndedAt is a faithful copy of
                // Session.EndedAt, never a filtered one (Gap 86). Every reader compares it to
                // End itself, so an on-time end changes no answer.
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

        var actualElapsedMinutes = session.GetElapsedTime(session.EndedAt!.Value)?.TotalMinutes ?? 0;

        // Reuses the exact same rule Daily Floor credit already applies (Gap H) — a
        // ritual completed within the grace period credits the block's full scheduled
        // duration, not (EndTime - RitualCompletedAt); one source of truth for "how much
        // focus time does this session count for," not a second copy of the rule invented
        // for display. Only applies to a Planned session with a resolved originating
        // block — an impromptu session has no scheduled window to be graced against.
        var creditedMinutes = originatingFocusBlock is not null
            ? _classifier.CreditedFocusMinutes(
            originatingFocusBlock.Scheduled, originatingFocusBlock.RitualCompletedAt, session.EndedAt, actualElapsedMinutes)
            : actualElapsedMinutes;

        var postSession = new PostSessionDto
        {
            SessionId = session.Id,
            Status = session.Status,
            Goal = session.Goal.Value,
            PresetName = presetName,
            StartedAt = session.StartedAt!.Value,
            EndedAt = session.EndedAt!.Value,
            PlannedDurationMinutes = session.Duration.Value.TotalMinutes,
            ActualDurationMinutes = creditedMinutes,
            BreaksTaken = session.BreaksTaken,
            BlockedAppAttempts = appAttempts,
            BlockedWebsiteAttempts = websiteAttempts,
            TotalBlockedAttempts = attempts.Count
        };

        return EndSessionResult.Ok(postSession);
    }
}