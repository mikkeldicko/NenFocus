using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed class ResumeFromBreakCommandHandler : ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionStateService _sessionState;
    private readonly ISystemClock _clock;

    public ResumeFromBreakCommandHandler(
        ISessionRepository sessions,
        ISessionStateService sessionState,
        ISystemClock clock)
    {
        _sessions = sessions;
        _sessionState = sessionState;
        _clock = clock;
    }

    public async Task<ResumeFromBreakResult> HandleAsync(ResumeFromBreakCommand command, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session == null) return ResumeFromBreakResult.Fail("Session not found.");

        if (session.Status != SessionStatus.OnBreak)
            return ResumeFromBreakResult.Fail("Session is not on a break.");

        session.ResumeFromBreak(_clock.UtcNow);
        await _sessions.SaveAsync(session, ct);

        var current = _sessionState.ActiveSession;
        if (current is not null)
        {
            _sessionState.SetActive(current with
            {
                Status = SessionStatus.Active,
                BreakStartedAt = null,
                CurrentBreakKind = null
            });
        }

        return ResumeFromBreakResult.Ok();
    }
}
