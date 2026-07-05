using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.SaveReflection;

public sealed class SaveReflectionCommandHandler : ICommandHandler<SaveReflectionCommand, SaveReflectionResult>
{
    private readonly ISessionRepository _sessions;

    public SaveReflectionCommandHandler(ISessionRepository sessions)
    {
        _sessions = sessions;
    }

    public async Task<SaveReflectionResult> HandleAsync(SaveReflectionCommand command, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session == null) return SaveReflectionResult.Fail("Session not found.");

        if (session.Status != SessionStatus.Completed && session.Status != SessionStatus.Missed)
            return SaveReflectionResult.Fail("Reflection can only be set on a completed or missed session.");

        session.SetReflection(command.Reflection);

        await _sessions.SaveAsync(session, ct);
        return SaveReflectionResult.Ok();
    }
}
