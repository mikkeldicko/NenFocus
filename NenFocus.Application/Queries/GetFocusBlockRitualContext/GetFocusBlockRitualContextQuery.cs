using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed record GetFocusBlockRitualContextQuery : IQuery<FocusBlockRitualContext?>
{
    public Guid FocusBlockId { get; init; }
}
