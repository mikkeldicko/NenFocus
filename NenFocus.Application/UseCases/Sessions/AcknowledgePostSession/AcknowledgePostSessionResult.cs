using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;

public sealed record AcknowledgePostSessionResult : ICommandResult<AcknowledgePostSessionResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static AcknowledgePostSessionResult Ok() => new() { Success = true };
    public static AcknowledgePostSessionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
