using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockCommand
{
    public Guid ProfileId { get; init; }
    public BlockType BlockType { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}
