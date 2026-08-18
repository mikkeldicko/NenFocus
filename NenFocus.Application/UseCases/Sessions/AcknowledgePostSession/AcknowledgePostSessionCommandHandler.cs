using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;

public sealed class AcknowledgePostSessionCommandHandler
    : ICommandHandler<AcknowledgePostSessionCommand, AcknowledgePostSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;

    public AcknowledgePostSessionCommandHandler(ISessionRepository sessions, ISystemClock clock)
    {
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<AcknowledgePostSessionResult> HandleAsync(AcknowledgePostSessionCommand command, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session == null) return AcknowledgePostSessionResult.Fail("Session not found.");

        if (session.Status != SessionStatus.Completed && session.Status != SessionStatus.Missed)
            return AcknowledgePostSessionResult.Fail("Post-session can only be acknowledged for a completed or missed session.");

        session.MarkPostSessionAcknowledged(_clock.UtcNow);

        await _sessions.SaveAsync(session, ct);
        return AcknowledgePostSessionResult.Ok();
    }
}
