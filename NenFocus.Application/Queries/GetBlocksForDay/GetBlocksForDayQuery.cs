using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetBlocksForDay;

public sealed record GetBlocksForDayQuery : IQuery<IReadOnlyList<BlockDto>>
{
    public DateOnly Date { get; init; }
}
