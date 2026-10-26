using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.UpdateBlock;

public sealed record UpdateBlockCommand : ICommand<UpdateBlockResult>
{
    public Guid BlockId { get; init; }
    public Guid PresetId { get; init; }
    public required TimeSlot Slot { get; init; }
}
