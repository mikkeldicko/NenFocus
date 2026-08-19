using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed record SplitPlannedBlockCommand : ICommand<SplitPlannedBlockResult>
{
    public Guid OriginalBlockId { get; init; }
    public DateTime FirstBlockStart { get; init; }
    public DateTime FirstBlockEnd { get; init; }
    public DateTime SecondBlockStart { get; init; }
    public DateTime SecondBlockEnd { get; init; }
}
