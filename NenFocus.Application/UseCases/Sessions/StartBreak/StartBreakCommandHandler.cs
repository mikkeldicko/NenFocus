using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed class StartBreakCommandHandler
    : ICommandHandler<StartBreakCommand, StartBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionStateService _sessionState;

    public StartBreakCommandHandler(
        ISessionRepository sessions,
        ISessionStateService sessionState)
    {
        _sessions = sessions;
        _sessionState = sessionState;
    }

    public async Task<StartBreakResult> HandleAsync(
        StartBreakCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null)
            return new StartBreakResult(false, "Session not found.");

        if (session.Status != SessionStatus.Active)
            return new StartBreakResult(false, "Can only start a break from an active session.");

        session.StartBreak(DateTime.UtcNow);

        await _sessions.SaveAsync(session, ct);

        var current = _sessionState.ActiveSession;
        if (current is not null)
        {
            _sessionState.SetActive(current with
            {
                Status = SessionStatus.OnBreak,
                BreakStartedAt = session.BreakStartedAt,
                BreaksTaken = session.BreaksTaken
            });
        }

        return new StartBreakResult(true);
    }
}