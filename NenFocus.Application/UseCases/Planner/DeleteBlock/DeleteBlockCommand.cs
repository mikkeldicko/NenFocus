using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeleteBlock;

public sealed record DeleteBlockCommand : ICommand<DeleteBlockResult>
{
    public Guid BlockId { get; init; }
}
