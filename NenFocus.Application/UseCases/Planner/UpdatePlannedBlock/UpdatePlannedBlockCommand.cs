using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockCommand : ICommand<UpdatePlannedBlockResult>
{
    public Guid BlockId { get; init; }
    public Guid PresetId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}
