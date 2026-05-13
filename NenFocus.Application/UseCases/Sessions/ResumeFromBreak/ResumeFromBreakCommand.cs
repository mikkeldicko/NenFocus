using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed record ResumeFromBreakCommand : ICommand<ResumeFromBreakResult>
{
    public Guid SessionId { get; init; }
}