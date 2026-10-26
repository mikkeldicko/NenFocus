using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.SplitBlock;

public sealed record SplitBlockResult : ICommandResult<SplitBlockResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? FirstBlockId { get; init; }
    public Guid? SecondBlockId { get; init; }

    public static SplitBlockResult Ok(Guid firstBlockId, Guid secondBlockId) =>
        new() { Success = true, FirstBlockId = firstBlockId, SecondBlockId = secondBlockId };

    public static SplitBlockResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
