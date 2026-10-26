using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed record DeletePlannedBlockResult : ICommandResult<DeletePlannedBlockResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeletePlannedBlockResult Ok() => new() { Success = true };
    public static DeletePlannedBlockResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
