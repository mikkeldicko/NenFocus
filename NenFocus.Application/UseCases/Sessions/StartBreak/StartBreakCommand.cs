using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed record StartBreakCommand : ICommand<StartBreakResult>
{
    public Guid SessionId { get; init; }
    // Add this if your UI is trying to set it:
    public bool UserInitiated { get; init; }
}