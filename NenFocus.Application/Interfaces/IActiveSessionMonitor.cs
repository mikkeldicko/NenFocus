using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface IActiveSessionMonitor
{
    TimeSpan SessionTimeRemaining { get; }
    double SessionProgress { get; }
    TimeSpan BreakTimeRemaining { get; }
    double BreakProgress { get; }

    /// <summary>
    /// Empty when the session has no BreakTimer ("No timer"). Otherwise one
    /// entry per break in IBreakScheduleCalculator's phase timeline for this
    /// session — the same timeline this monitor already walks to drive automatic
    /// work/break transitions, so the Main Page never re-derives break positions
    /// itself.
    /// </summary>
    IReadOnlyList<BreakMarkerDto> BreakMarkers { get; }

    event EventHandler? Tick;
    event EventHandler<PostSessionDto>? SessionAutoEnded;
}
