using System.Text.Json;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed record EndSessionCommand
{
    public Guid SessionId { get; init; }
}

public sealed record EndSessionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public PostSessionDto? PostSession { get; init; }

    public static EndSessionResult Ok(PostSessionDto postSession) =>
        new() { Success = true, PostSession = postSession };
    public static EndSessionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class EndSessionCommandHandler
    : ICommandHandler<EndSessionCommand, EndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockProfileRepository _profiles;
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;
    private readonly ISessionStateService _sessionState;
    private readonly IBlockedAttemptTracker _attemptTracker;

    public EndSessionCommandHandler(
        ISessionRepository sessions,
        IBlockProfileRepository profiles,
        IEnforcementService enforcement,
        INotificationService notifications,
        ISessionStateService sessionState,
        IBlockedAttemptTracker attemptTracker)
    {
        _sessions = sessions;
        _profiles = profiles;
        _enforcement = enforcement;
        _notifications = notifications;
        _sessionState = sessionState;
        _attemptTracker = attemptTracker;
    }

    public async Task<EndSessionResult> HandleAsync(
        EndSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, cancellationToken);
        if (session is null)
            return EndSessionResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Active &&
            session.Status != SessionStatus.OnBreak)
            return EndSessionResult.Fail("Session is not currently active.");

        // 1. Fetch all blocked attempts for this session from the database
        var attempts = await _attemptTracker.GetAttemptsForSessionAsync(session.Id);

        // 2. Group them by app name for the summary (Matches your old Dictionary structure)
        var appAttempts = attempts
     .Where(a => a.AttemptType == BlockedAttemptType.App)
     .GroupBy(a => a.ResourceName)
     .ToDictionary(g => g.Key, g => g.Count());

        // ADD THIS: Group Website attempts
        var websiteAttempts = attempts
            .Where(a => a.AttemptType == BlockedAttemptType.Website)
            .GroupBy(a => a.ResourceName)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalAttempts = attempts.Count;
       
        var historyData = new { Apps = appAttempts, Websites = websiteAttempts };
        session.SetBlockedAttempts(JsonSerializer.Serialize(historyData));

        // Transition session to Completed
        session.Complete();

        // Stop blocking and cancel notifications
        await _enforcement.RemoveEnforcementAsync(cancellationToken);
        await _notifications.CancelAllRemindersAsync(cancellationToken);
        await _sessions.SaveAsync(session, cancellationToken);

        // NOTE: _attemptTracker.Reset() is removed because we are using a database now.

        // Look up profile name for the post-session screen
        var profileName = string.Empty;
        if (session.BlockProfileId != Guid.Empty)
        {
            var profile = await _profiles.GetByIdAsync(session.BlockProfileId, cancellationToken);
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
            ActualDurationMinutes = (int)(session.EndedAt!.Value - session.StartedAt!.Value).TotalMinutes,
            BreaksTaken = session.BreaksTaken,
            BlockedAppAttempts = appAttempts,
            BlockedWebsiteAttempts = websiteAttempts, 
            TotalBlockedAttempts = totalAttempts
        };

        // Notify the UI layer that the session has ended
        _sessionState.Clear();

        return EndSessionResult.Ok(postSession);
    }
}