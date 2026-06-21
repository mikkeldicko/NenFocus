using NenFocus.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;

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
        if (session == null) return new SaveReflectionResult(false, "Session not found.");

        session.SetReflection(command.Reflection, command.Rating);

        await _sessions.SaveAsync(session, ct);
        return new SaveReflectionResult(true);
    }
}