using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.SaveReflection;

public sealed record SaveReflectionCommand
{
    public Guid SessionId { get; init; }
    public string? Reflection { get; init; }
}

public sealed record SaveReflectionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static SaveReflectionResult Ok() => new() { Success = true };
    public static SaveReflectionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed class SaveReflectionCommandHandler
    : ICommandHandler<SaveReflectionCommand, SaveReflectionResult>
{
    private readonly ISessionRepository _sessions;

    public SaveReflectionCommandHandler(ISessionRepository sessions)
        => _sessions = sessions;

    public async Task<SaveReflectionResult> HandleAsync(
        SaveReflectionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, cancellationToken);
        if (session is null)
            return SaveReflectionResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Completed)
            return SaveReflectionResult.Fail("Can only save reflection for a completed session.");

        session.SetReflection(command.Reflection);
        await _sessions.SaveAsync(session, cancellationToken);

        return SaveReflectionResult.Ok();
    }
}
