using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed record EndSessionResult : ICommandResult<EndSessionResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public PostSessionDto? PostSession { get; init; }

    public static EndSessionResult Ok(PostSessionDto postSession) =>
        new() { Success = true, PostSession = postSession };
    public static EndSessionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
