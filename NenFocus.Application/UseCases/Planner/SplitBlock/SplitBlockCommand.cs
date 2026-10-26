using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed record SplitPlannedBlockCommand : ICommand<SplitPlannedBlockResult>
{
    public Guid OriginalBlockId { get; init; }
    public required TimeSlot First { get; init; }
    public required TimeSlot Second { get; init; }
}
