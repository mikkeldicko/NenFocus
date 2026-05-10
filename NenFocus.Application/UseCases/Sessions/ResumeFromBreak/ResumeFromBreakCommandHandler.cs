using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed class ResumeFromBreakCommandHandler : ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IEnforcementService _enforcement;
    private readonly IBlockProfileRepository _profiles;

    public ResumeFromBreakCommandHandler(
        ISessionRepository sessions,
        IEnforcementService enforcement,
        IBlockProfileRepository profiles)
    {
        _sessions = sessions;
        _enforcement = enforcement;
        _profiles = profiles;
    }

    public async Task<ResumeFromBreakResult> HandleAsync(ResumeFromBreakCommand command, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session == null) return new ResumeFromBreakResult(false, "Session not found.");

        if (session.Status != SessionStatus.OnBreak)
            return new ResumeFromBreakResult(false, "Session is not on a break.");

        // FIX: Match the domain method name exactly
        session.ResumeFromBreak();

        await _sessions.SaveAsync(session, ct);
        return new ResumeFromBreakResult(true);
    }
}