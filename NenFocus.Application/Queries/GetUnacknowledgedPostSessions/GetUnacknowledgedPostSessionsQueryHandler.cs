using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetUnacknowledgedPostSessions;

public sealed class GetUnacknowledgedPostSessionsQueryHandler
    : IQueryHandler<GetUnacknowledgedPostSessionsQuery, IReadOnlyList<PostSessionDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly IBlockedAttemptTracker _attemptTracker;
    private readonly IFocusBlockOutcomeClassifier _classifier;

    public GetUnacknowledgedPostSessionsQueryHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        IPlannedBlockRepository plannedBlocks,
        IBlockedAttemptTracker attemptTracker,
        IFocusBlockOutcomeClassifier classifier)
    {
        _sessions = sessions;
        _presets = presets;
        _plannedBlocks = plannedBlocks;
        _attemptTracker = attemptTracker;
        _classifier = classifier;
    }

    public async Task<IReadOnlyList<PostSessionDto>> HandleAsync(
        GetUnacknowledgedPostSessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _sessions.GetUnacknowledgedPostSessionsAsync(cancellationToken);
        var presetNames = new Dictionary<Guid, string>();

        var results = new List<PostSessionDto>(sessions.Count);
        foreach (var session in sessions)
            results.Add(await BuildDtoAsync(session, presetNames, cancellationToken));

        return results;
    }

    private async Task<PostSessionDto> BuildDtoAsync(
        Session session,
        Dictionary<Guid, string> presetNames,
        CancellationToken ct)
    {
        var presetName = await ResolvePresetNameAsync(session.BlockPresetId, presetNames, ct);

        var attempts = await _attemptTracker.GetAttemptsForSessionAsync(session.Id);
        var appAttempts = attempts
            .Where(a => a.AttemptType == BlockedAttemptType.App)
            .GroupBy(a => a.ResourceName)
            .ToDictionary(g => g.Key, g => g.Count());
        var websiteAttempts = attempts
            .Where(a => a.AttemptType == BlockedAttemptType.Website)
            .GroupBy(a => a.ResourceName)
            .ToDictionary(g => g.Key, g => g.Count());

        var actualDurationMinutes = await ResolveActualDurationMinutesAsync(session, ct);

        return new PostSessionDto
        {
            SessionId = session.Id,
            Status = session.Status,
            Goal = session.Goal.Value,
            PresetName = presetName,
            StartedAt = session.StartedAt!.Value,
            EndedAt = session.EndedAt!.Value,
            PlannedDurationMinutes = session.Duration.Value.TotalMinutes,
            ActualDurationMinutes = actualDurationMinutes,
            BreaksTaken = session.BreaksTaken,
            BlockedAppAttempts = appAttempts,
            BlockedWebsiteAttempts = websiteAttempts,
            TotalBlockedAttempts = attempts.Count
        };
    }

    private async Task<double> ResolveActualDurationMinutesAsync(Session session, CancellationToken ct)
    {
        var actualElapsedMinutes = session.GetElapsedTime(session.EndedAt!.Value)?.TotalMinutes ?? 0;

        if (session.Status != SessionStatus.Completed || !session.OriginatingBlockId.HasValue)
            return actualElapsedMinutes;

        var block = await _plannedBlocks.GetByIdAsync(session.OriginatingBlockId.Value, ct);
        if (block is not FocusBlock focusBlock)
            return actualElapsedMinutes;

        return _classifier.CreditedFocusMinutes(
            focusBlock.Scheduled, focusBlock.RitualCompletedAt, session.EndedAt, actualElapsedMinutes);
    }

    private async Task<string> ResolvePresetNameAsync(
        Guid presetId,
        Dictionary<Guid, string> cache,
        CancellationToken ct)
    {
        if (presetId == Guid.Empty)
            return string.Empty;

        if (cache.TryGetValue(presetId, out var cached))
            return cached;

        var preset = await _presets.GetByIdAsync(presetId, ct);
        var name = preset?.Name ?? string.Empty;
        cache[presetId] = name;
        return name;
    }
}
