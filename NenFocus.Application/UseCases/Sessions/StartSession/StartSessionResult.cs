using System;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed record StartSessionResult
{
    public bool Success { get; init; }
    public Guid? SessionId { get; init; }
    public string? ErrorMessage { get; init; }

    public static StartSessionResult Ok(Guid sessionId) =>
        new() { Success = true, SessionId = sessionId };
    public static StartSessionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}