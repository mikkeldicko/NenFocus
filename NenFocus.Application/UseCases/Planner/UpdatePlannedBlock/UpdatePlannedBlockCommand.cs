using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockCommand : ICommand<UpdatePlannedBlockResult>
{
    public Guid BlockId { get; init; }
    public Guid ProfileId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}
