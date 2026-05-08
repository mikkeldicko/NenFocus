using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed record StartBreakCommand
{
    public Guid SessionId { get; init; }
    public bool UserInitiated { get; init; }
}

public sealed record StartBreakResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static StartBreakResult Ok() => new() { Success = true };
    public static StartBreakResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed class StartBreakCommandHandler
    : ICommandHandler<StartBreakCommand, StartBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionStateService _sessionState;
    private readonly INotificationService _notifications;

    public StartBreakCommandHandler(
        ISessionRepository sessions,
        ISessionStateService sessionState,
        INotificationService notifications)
    {
        _sessions = sessions;
        _sessionState = sessionState;
        _notifications = notifications;
    }

    public async Task<StartBreakResult> HandleAsync(
        StartBreakCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, cancellationToken);
        if (session is null)
            return StartBreakResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Active)
            return StartBreakResult.Fail("No active session to start a break for.");

        if (session.BreakSettings is null)
            return StartBreakResult.Fail("No break settings configured for this session.");

        session.StartBreak();
        await _sessions.SaveAsync(session, cancellationToken);

        // Send break notification
        await _notifications.NotifyAsync(
            title: "Break time",
            body: $"Break #{session.BreaksTaken} — step away and recharge. ({session.BreakSettings.BreakLengthMinutes} min)",
            cancellationToken: cancellationToken);

        // Update session state so the UI reflects OnBreak status
        var current = _sessionState.ActiveSession;
        if (current is not null)
        {
            _sessionState.SetActive(current with
            {
                Status = "OnBreak",
                BreaksTaken = session.BreaksTaken,
                BreakStartedAt = session.BreakStartedAt
            });
        }

        return StartBreakResult.Ok();
    }
}
