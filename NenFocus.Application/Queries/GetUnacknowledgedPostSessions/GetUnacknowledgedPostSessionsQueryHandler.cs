using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetUnacknowledgedPostSessions;

public sealed class GetUnacknowledgedPostSessionsQueryHandler
    : IQueryHandler<GetUnacknowledgedPostSessionsQuery, IReadOnlyList<PostSessionDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly IBlockedAttemptTracker _attemptTracker;

    public GetUnacknowledgedPostSessionsQueryHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        IPlannedBlockRepository plannedBlocks,
        IBlockedAttemptTracker attemptTracker)
    {
        _sessions = sessions;
        _presets = presets;
        _plannedBlocks = plannedBlocks;
        _attemptTracker = attemptTracker;
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
        var originatingBlock = session.OriginatingBlockId is { } blockId
            ? await _plannedBlocks.GetByIdAsync(blockId, ct) as FocusBlock
            : null;

        return PostSessionDto.From(session, originatingBlock, presetName, attempts);
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
