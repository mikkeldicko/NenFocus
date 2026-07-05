using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed record MarkFocusBlockMissedResult : ICommandResult<MarkFocusBlockMissedResult>
{
    public bool Success { get; init; }
    public Guid? SessionId { get; init; }
    public string? ErrorMessage { get; init; }

    public static MarkFocusBlockMissedResult Ok(Guid sessionId) => new() { Success = true, SessionId = sessionId };
    public static MarkFocusBlockMissedResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
