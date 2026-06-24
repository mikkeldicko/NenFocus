using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.SaveReflection;

public sealed record SaveReflectionResult : ICommandResult<SaveReflectionResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static SaveReflectionResult Ok() => new() { Success = true };
    public static SaveReflectionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
