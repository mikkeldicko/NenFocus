namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed record ResumeFromBreakCommand
{
    public Guid SessionId { get; init; }
}