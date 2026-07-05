using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed record MarkFocusBlockMissedCommand : ICommand<MarkFocusBlockMissedResult>
{
    public Guid FocusBlockId { get; init; }
}
