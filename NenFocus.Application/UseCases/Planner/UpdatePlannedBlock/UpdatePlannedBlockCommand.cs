using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockCommand : ICommand<UpdatePlannedBlockResult>
{
    public Guid BlockId { get; init; }
    public Guid PresetId { get; init; }
    public required TimeSlot Slot { get; init; }
}
