using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed record ResumeFromBreakResult : ICommandResult<ResumeFromBreakResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ResumeFromBreakResult Ok() => new() { Success = true };
    public static ResumeFromBreakResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
