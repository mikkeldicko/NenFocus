using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed class ResumeFromBreakCommandHandler : ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionStateService _sessionState;

    public ResumeFromBreakCommandHandler(
           ISessionRepository sessions,
           ISessionStateService sessionState)
    {
        _sessions = sessions;
        _sessionState = sessionState;
    }
    public async Task<ResumeFromBreakResult> HandleAsync(ResumeFromBreakCommand command, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session == null) return new ResumeFromBreakResult(false, "Session not found.");

        if (session.Status != SessionStatus.OnBreak)
            return new ResumeFromBreakResult(false, "Session is not on a break.");

        session.ResumeFromBreak();
        await _sessions.SaveAsync(session, ct);

        var current = _sessionState.ActiveSession;
        if (current is not null)
        {
            _sessionState.SetActive(current with
            {
                Status = SessionStatus.Active,
                BreakStartedAt = null
            });
        }

        return new ResumeFromBreakResult(true);
    }
}