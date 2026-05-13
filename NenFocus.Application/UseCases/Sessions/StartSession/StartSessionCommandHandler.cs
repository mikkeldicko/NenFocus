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
    private readonly IDomainEventPublisher _publisher;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISessionStateService sessionState,
        IBlockProfileRepository profiles,
        IDomainEventPublisher publisher)
    {
        _sessions = sessions;
        _plannedBlocks = plannedBlocks;
        _sessionState = sessionState;
        _profiles = profiles;
        _publisher = publisher;
    }

    public async Task<StartSessionResult> HandleAsync(
        StartSessionCommand command,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var requestedEnd = now.AddMinutes(command.DurationMinutes);

        // Check for conflicting RitualBlocks ahead
        var upcomingBlocks = await _plannedBlocks.GetForRangeAsync(now, requestedEnd, ct);
        var conflictingBlock = upcomingBlocks
            .OfType<RitualBlock>()
            .OrderBy(b => b.Start)
            .FirstOrDefault();

        if (conflictingBlock is not null)
        {
            var minutesAvailable = (int)(conflictingBlock.Start - now).TotalMinutes;
            if (minutesAvailable > 0)
                return StartSessionResult.Fail(
                    $"A planned ritual block starts in {minutesAvailable} minutes. " +
                    $"Please set your duration to {minutesAvailable} minutes or less.");
        }

        BreakSettings? breakSettings = null;
        if (command.BreakSettings is { Enabled: true })
        {
            breakSettings = new BreakSettings(
                command.BreakSettings.IntervalMinutes,
                command.BreakSettings.BreakDurationMinutes);
        }

        // Determine origin — if a RitualBlock triggered this, it is Planned
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
            breakSettings: breakSettings,
            temporaryBlockedApps: command.TemporaryBlockedApps.ToList(),
            temporaryBlockedSites: command.TemporaryBlockedSites.ToList());

        session.Start(now);

        // If this was triggered by a RitualBlock, mark it completed
        if (command.OriginatingBlockId.HasValue)
        {
            var ritualBlock = await _plannedBlocks.GetByIdAsync(
                command.OriginatingBlockId.Value, ct) as RitualBlock;

            ritualBlock?.MarkRitualCompleted(now);

            if (ritualBlock is not null)
                await _plannedBlocks.SaveAsync(ritualBlock, ct);
        }

        await _sessions.SaveAsync(session, ct);

        await _publisher.PublishAllAsync(session.DomainEvents, ct);
        session.ClearDomainEvents();

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
            BreakSettings = command.BreakSettings,
            TemporaryBlockedApps = command.TemporaryBlockedApps,
            TemporaryBlockedSites = command.TemporaryBlockedSites
        });

        return StartSessionResult.Ok(session.Id);
    }
}