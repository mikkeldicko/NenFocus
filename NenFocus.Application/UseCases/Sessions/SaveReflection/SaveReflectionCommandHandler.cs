using NenFocus.Application.Interfaces;

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

        session.SetReflection(command.Reflection);

        await _sessions.SaveAsync(session, ct);
        return SaveReflectionResult.Ok();
    }
}
