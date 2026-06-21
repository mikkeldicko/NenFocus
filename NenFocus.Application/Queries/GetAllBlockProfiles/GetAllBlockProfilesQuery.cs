using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllBlockProfiles;

public sealed record GetAllBlockProfilesQuery : IQuery<IReadOnlyList<BlockProfileDto>>
{
    public string? Tag { get; init; }
}
