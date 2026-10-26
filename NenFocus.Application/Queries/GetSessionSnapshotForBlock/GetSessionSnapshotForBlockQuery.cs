using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetSessionSnapshotForBlock;

public sealed record GetSessionSnapshotForBlockQuery : IQuery<SessionSnapshotDto?>
{
    public Guid BlockId { get; init; }
}
