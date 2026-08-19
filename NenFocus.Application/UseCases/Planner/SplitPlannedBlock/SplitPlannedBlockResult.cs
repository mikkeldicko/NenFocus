using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed record SplitPlannedBlockResult : ICommandResult<SplitPlannedBlockResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? FirstBlockId { get; init; }
    public Guid? SecondBlockId { get; init; }

    public static SplitPlannedBlockResult Ok(Guid firstBlockId, Guid secondBlockId) =>
        new() { Success = true, FirstBlockId = firstBlockId, SecondBlockId = secondBlockId };

    public static SplitPlannedBlockResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
