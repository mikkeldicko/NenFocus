using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed record ResumeFromBreakCommand
{
    public Guid SessionId { get; init; }
}

public sealed record ResumeFromBreakResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ResumeFromBreakResult Ok() => new() { Success = true };
    public static ResumeFromBreakResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed class ResumeFromBreakCommandHandler
    : ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionStateService _sessionState;
    private readonly INotificationService _notifications;

    public ResumeFromBreakCommandHandler(
        ISessionRepository sessions,
        ISessionStateService sessionState,
        INotificationService notifications)
    {
        _sessions = sessions;
        _sessionState = sessionState;
        _notifications = notifications;
    }

    public async Task<ResumeFromBreakResult> HandleAsync(
        ResumeFromBreakCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, cancellationToken);
        if (session is null)
            return ResumeFromBreakResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.OnBreak)
            return ResumeFromBreakResult.Fail("Session is not currently on a break.");

        session.ResumeFromBreak();
        await _sessions.SaveAsync(session, cancellationToken);

        await _notifications.NotifyAsync(
            title: "Break over",
            body: "Time to refocus — your session is resuming.",
            cancellationToken: cancellationToken);

        // Update session state so the UI reflects Active status
        var current = _sessionState.ActiveSession;
        if (current is not null)
        {
            _sessionState.SetActive(current with
            {
                Status = "Active",
                BreakStartedAt = null
            });
        }

        return ResumeFromBreakResult.Ok();
    }
}
