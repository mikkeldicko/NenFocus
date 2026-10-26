using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed record DeletePlannedBlockCommand : ICommand<DeletePlannedBlockResult>
{
    public Guid BlockId { get; init; }
}
