using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeleteBlock;

public sealed record DeleteBlockResult : ICommandResult<DeleteBlockResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteBlockResult Ok() => new() { Success = true };
    public static DeleteBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
