namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed record DeletePlannedBlockCommand
{
    public Guid BlockId { get; init; }
}
