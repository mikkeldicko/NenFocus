using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetAllBlockProfiles;

public sealed record GetAllBlockProfilesQuery : IQuery<IReadOnlyList<BlockProfileDto>>
{
    public string? Tag { get; init; }
    public BlockType? ProfileType { get; init; }
}
