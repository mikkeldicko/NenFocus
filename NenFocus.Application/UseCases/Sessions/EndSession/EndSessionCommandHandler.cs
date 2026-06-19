using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed class EndSessionCommandHandler
    : ICommandHandler<EndSessionCommand, EndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockProfileRepository _profiles;
    private readonly ISessionStateService _sessionState;
    private readonly IBlockedAttemptTracker _attemptTracker;

    public EndSessionCommandHandler(
        ISessionRepository sessions,
        IBlockProfileRepository profiles,
        ISessionStateService sessionState,
        IBlockedAttemptTracker attemptTracker)
    {
        _sessions = sessions;
        _profiles = profiles;
        _sessionState = sessionState;
        _attemptTracker = attemptTracker;
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

        session.Complete(DateTime.UtcNow);

        await _sessions.SaveAsync(session, ct);

        var profileName = string.Empty;
        if (session.BlockProfileId != Guid.Empty)
        {
            var profile = await _profiles.GetByIdAsync(session.BlockProfileId, ct);
            profileName = profile?.Name ?? string.Empty;
        }

        var postSession = new PostSessionDto
        {
            SessionId = session.Id,
            Goal = session.Goal.Value,
            ProfileName = profileName,
            StartedAt = session.StartedAt!.Value,
            EndedAt = session.EndedAt!.Value,
            PlannedDurationMinutes = (int)session.Duration.Value.TotalMinutes,
            ActualDurationMinutes = (int)(
                session.EndedAt.Value - session.StartedAt.Value).TotalMinutes,
            BreaksTaken = session.BreaksTaken,
            BlockedAppAttempts = appAttempts,
            BlockedWebsiteAttempts = websiteAttempts,
            TotalBlockedAttempts = attempts.Count
        };

        _sessionState.Clear();

        return EndSessionResult.Ok(postSession);
    }
}