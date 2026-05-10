namespace NenFocus.Application.UseCases.Sessions.StartBreak;

public sealed record StartBreakCommand
{
    public Guid SessionId { get; init; }
    // Add this if your UI is trying to set it:
    public bool UserInitiated { get; init; }
}