using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed record MarkFocusBlockMissedResult : ICommandResult<MarkFocusBlockMissedResult>
{
    public bool Success { get; init; }
    public Guid? SessionId { get; init; }
    public string? ErrorMessage { get; init; }
    public PostSessionDto? PostSession { get; init; }

    public static MarkFocusBlockMissedResult Ok(Guid sessionId, PostSessionDto postSession) =>
        new() { Success = true, SessionId = sessionId, PostSession = postSession };
    public static MarkFocusBlockMissedResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
