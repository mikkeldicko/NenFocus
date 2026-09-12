using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed class StartBreakCommandHandler
    : ICommandHandler<StartBreakCommand, StartBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionStateService _sessionState;
    private readonly ISystemClock _clock;

    public StartBreakCommandHandler(
        ISessionRepository sessions,
        ISessionStateService sessionState,
        ISystemClock clock)
    {
        _sessions = sessions;
        _sessionState = sessionState;
        _clock = clock;
    }

    public async Task<StartBreakResult> HandleAsync(
        StartBreakCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null)
            return StartBreakResult.Fail("Session not found.");

        if (session.Status != SessionStatus.Active)
            return StartBreakResult.Fail("Can only start a break from an active session.");

        if (session.BreakTimer is null)
            return StartBreakResult.Fail("Cannot start a break — no timer cadence configured.");

        session.StartBreak(_clock.UtcNow, command.BreakKind);

        await _sessions.SaveAsync(session, ct);

        var current = _sessionState.ActiveSession;
        if (current is not null)
        {
            _sessionState.SetActive(current with
            {
                Status = SessionStatus.OnBreak,
                BreakStartedAt = session.BreakStartedAt,
                BreaksTaken = session.BreaksTaken,
                CurrentBreakKind = session.CurrentBreakKind
            });
        }

        return StartBreakResult.Ok();
    }
}