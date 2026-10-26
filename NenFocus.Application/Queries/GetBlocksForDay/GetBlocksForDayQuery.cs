using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetPlannedBlocksForDay;

public sealed record GetPlannedBlocksForDayQuery : IQuery<IReadOnlyList<PlannedBlockDto>>
{
    public DateOnly Date { get; init; }
}
