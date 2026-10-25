using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed record EndSessionResult : ICommandResult<EndSessionResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static EndSessionResult Ok() => new() { Success = true };
    public static EndSessionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
