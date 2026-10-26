using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.SplitBlock;

public sealed record SplitBlockCommand : ICommand<SplitBlockResult>
{
    public Guid OriginalBlockId { get; init; }
    public required TimeSlot First { get; init; }
    public required TimeSlot Second { get; init; }
}
