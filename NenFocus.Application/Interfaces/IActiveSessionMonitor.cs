using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface IActiveSessionMonitor
{
    TimeSpan SessionTimeRemaining { get; }
    double SessionProgress { get; }

    /// <summary>True while the clock is inside one of the session's break slots. The
    /// session itself stays Active throughout — a break is a stretch of its schedule,
    /// not a state (Gap 63).</summary>
    bool IsOnBreak { get; }

    /// <summary>True during a break whose session keeps its preset blocked through breaks
    /// (its contract's rule, Gap 64) — so the Main Page can say so.</summary>
    bool KeepsBlockingDuringBreak { get; }
    TimeSpan BreakTimeRemaining { get; }
    double BreakProgress { get; }

    /// <summary>Breaks begun so far this session.</summary>
    int BreaksTaken { get; }

    /// <summary>
    /// Empty for "No timer". Otherwise one entry per break in the session's own
    /// BreakSchedule — the same list that decides when it is on a break, so the Main
    /// Page never re-derives break positions itself.
    /// </summary>
    IReadOnlyList<BreakMarkerDto> BreakMarkers { get; }

    event EventHandler? Tick;
    event EventHandler<PostSessionDto>? SessionAutoEnded;
}
