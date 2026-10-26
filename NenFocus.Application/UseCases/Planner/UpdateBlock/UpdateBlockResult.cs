using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdateBlock;

public sealed record UpdateBlockResult : ICommandResult<UpdateBlockResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateBlockResult Ok() => new() { Success = true };
    public static UpdateBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
