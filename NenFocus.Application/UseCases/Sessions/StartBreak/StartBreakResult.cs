using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed record StartBreakResult : ICommandResult<StartBreakResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static StartBreakResult Ok() => new() { Success = true };
    public static StartBreakResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
