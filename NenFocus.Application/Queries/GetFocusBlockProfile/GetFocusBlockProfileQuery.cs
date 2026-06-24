using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetFocusBlockProfile;

public sealed record GetFocusBlockProfileQuery : IQuery<BlockProfileDto?>
{
    public Guid FocusBlockId { get; init; }
}
