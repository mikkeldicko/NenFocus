using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionCommandHandler
    : ICommandHandler<StartSessionCommand, StartSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly ISessionStateService _sessionState;
    private readonly IBlockProfileRepository _profiles;
    private readonly ISystemClock _clock;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISessionStateService sessionState,
        IBlockProfileRepository profiles,
        ISystemClock clock)
    {
        _sessions = sessions;
        _plannedBlocks = plannedBlocks;
        _sessionState = sessionState;
        _profiles = profiles;
        _clock = clock;
    }

    public async Task<StartSessionResult> HandleAsync(
        StartSessionCommand command,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var requestedEnd = now.AddMinutes(command.DurationMinutes);

        var upcomingBlocks = await _plannedBlocks.GetForRangeAsync(now, requestedEnd, ct);
        var conflictingBlock = upcomingBlocks
            .OfType<FocusBlock>()
            .OrderBy(b => b.Start)
            .FirstOrDefault();

        if (conflictingBlock is not null)
        {
            var minutesAvailable = conflictingBlock.MinutesUntilStart(now);
            if (minutesAvailable > 0)
                return StartSessionResult.Fail(
                    $"A planned focus block starts in {minutesAvailable} minutes. " +
                    $"Please set your duration to {minutesAvailable} minutes or less.");
        }

        BreakSettings? breakSettings = null;
        if (command.BreakSettings is { Enabled: true })
        {
            breakSettings = new BreakSettings(
                command.BreakSettings.IntervalMinutes,
                command.BreakSettings.BreakDurationMinutes);
        }

        var origin = command.OriginatingBlockId.HasValue
            ? SessionOrigin.Planned
            : SessionOrigin.Impromptu;

        var session = Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal(command.Goal),
            duration: new SessionDuration(TimeSpan.FromMinutes(command.DurationMinutes)),
            blockProfileId: command.ProfileId ?? Guid.Empty,
            origin: origin,
            originatingBlockId: command.OriginatingBlockId,
            breakSettings: breakSettings);

        session.Start(now);

        if (command.OriginatingBlockId.HasValue)
        {
            var focusBlock = await _plannedBlocks.GetByIdAsync(
                command.OriginatingBlockId.Value, ct) as FocusBlock;

            focusBlock?.MarkRitualCompleted(now);

            if (focusBlock is not null)
                await _plannedBlocks.SaveAsync(focusBlock, ct);
        }

        await _sessions.SaveAsync(session, ct);

        var profileName = string.Empty;
        if (command.ProfileId is { } pid && pid != Guid.Empty)
        {
            var profile = await _profiles.GetByIdAsync(pid, ct);
            profileName = profile?.Name ?? string.Empty;
        }

        _sessionState.SetActive(new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            ProfileName = profileName,
            DurationMinutes = command.DurationMinutes,
            StartedAt = session.StartedAt ?? now,
            Status = session.Status,
            Origin = session.Origin,
            OriginatingBlockId = session.OriginatingBlockId,
            BreakSettings = command.BreakSettings
        });

        return StartSessionResult.Ok(session.Id);
    }
}
