using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed class StartBreakCommandHandler : ICommandHandler<StartBreakCommand, StartBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IEnforcementService _enforcement;

    public StartBreakCommandHandler(ISessionRepository sessions, IEnforcementService enforcement)
    {
        _sessions = sessions;
        _enforcement = enforcement;
    }

    public async Task<StartBreakResult> HandleAsync(StartBreakCommand command, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session == null) return new StartBreakResult(false, "Session not found.");

        if (session.Status != SessionStatus.Active)
            return new StartBreakResult(false, "Can only start a break from an active session.");

        session.StartBreak();

        // Pause enforcement during break
        await _enforcement.RemoveEnforcementAsync(ct);
        await _sessions.SaveAsync(session, ct);

        return new StartBreakResult(true);
    }
}