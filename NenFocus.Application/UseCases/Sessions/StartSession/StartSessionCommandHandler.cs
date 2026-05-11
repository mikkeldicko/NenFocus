using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionCommandHandler : ICommandHandler<StartSessionCommand, StartSessionResult>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IPlannedBlockRepository _plannedBlockRepository;
    private readonly ISessionStateService _sessionState;

    public StartSessionCommandHandler(
        ISessionRepository sessionRepository,
        IPlannedBlockRepository plannedBlockRepository,
        ISessionStateService sessionState)
    {
        _sessionRepository = sessionRepository;
        _plannedBlockRepository = plannedBlockRepository;
        _sessionState = sessionState;
    }

    public async Task<StartSessionResult> HandleAsync(StartSessionCommand command, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var requestedDuration = TimeSpan.FromMinutes(command.DurationMinutes);
        var requestedEnd = now.Add(requestedDuration);

        // --- REQUIREMENT 4: Overlap Validation ---
        var upcomingBlocks = await _plannedBlockRepository.GetForRangeAsync(now, requestedEnd, ct);

        var conflictingBlock = upcomingBlocks
            .Where(b => b.Type == BlockType.Focus && b.IsPlanned)
            .OrderBy(b => b.Start)
            .FirstOrDefault();

        if (conflictingBlock != null)
        {
            var minutesAvailable = (int)(conflictingBlock.Start - now).TotalMinutes;

            if (minutesAvailable > 0)
            {
                return StartSessionResult.Fail(
                    $"A planned focus block starts in {minutesAvailable} minutes. " +
                    $"Please set your duration to {minutesAvailable} minutes or less.");
            }
        }

        // --- Proceed with Session Creation using your Domain Models ---

        // 1. Instantiate Value Objects
        var goal = new SessionGoal(command.Goal);
        var duration = new SessionDuration(requestedDuration);

        BreakSettings? breakSettings = null;
        if (command.BreakSettings != null && command.BreakSettings.Enabled)
        {
            breakSettings = new BreakSettings(
                command.BreakSettings.IntervalMinutes,
                command.BreakSettings.BreakDurationMinutes);
        }

        // 2. Create and Start the Session Entity
        var session = Session.Create(
            goal,
            duration,
            command.ProfileId ?? Guid.Empty,
            breakSettings);

        session.Start(); // Set status to Active and set Start timestamp

        // 3. Persist to Database
        await _sessionRepository.SaveAsync(session, ct);

        // 4. Update the global UI/Service state
        _sessionState.SetActive(new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            ProfileId = session.BlockProfileId.ToString(),
            DurationMinutes = command.DurationMinutes,
            StartedAt = session.StartedAt ?? now,
            Status = session.Status.ToString(),
            BreakSettings = command.BreakSettings,
            TemporaryBlockedApps = command.TemporaryBlockedApps,
            TemporaryBlockedSites = command.TemporaryBlockedSites
        });

        return StartSessionResult.Ok(session.Id);
    }
}