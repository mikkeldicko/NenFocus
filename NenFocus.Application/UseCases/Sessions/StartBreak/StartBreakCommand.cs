using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed record StartBreakCommand : ICommand<StartBreakResult>
{
    public Guid SessionId { get; init; }
    public bool UserInitiated { get; init; }
}