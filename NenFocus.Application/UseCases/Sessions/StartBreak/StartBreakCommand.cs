using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.StartBreak;

/// <summary>
/// Manual break-taking is retired app-wide — the only caller is the automatic
/// engine (ActiveSessionMonitor) that notices a Work phase has elapsed, mirroring
/// how it already auto-calls ResumeFromBreakCommand when a break's timer elapses.
/// BreakKind is resolved by the caller via IBreakScheduleCalculator, never
/// re-derived inside the handler.
/// </summary>
public sealed record StartBreakCommand : ICommand<StartBreakResult>
{
    public Guid SessionId { get; init; }
    public SessionPhaseKind BreakKind { get; init; }
}