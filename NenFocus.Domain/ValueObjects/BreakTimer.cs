using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A work/break rhythm — work, short break, cycles before a long break, long break. A
/// BreakTimerPreset is a saved, named one. A session never holds a BreakTimer itself:
/// ScheduleFor turns the rhythm into the concrete BreakSchedule the session owns, and
/// the session never needs to know which rhythm (if any) produced it (Gap 63).
/// </summary>
public record BreakTimer
{
    public int WorkMinutes { get; }
    public int ShortBreakMinutes { get; }
    public int CyclesBeforeLongBreak { get; }
    public int LongBreakMinutes { get; }

    public BreakTimer(int workMinutes, int shortBreakMinutes, int cyclesBeforeLongBreak, int longBreakMinutes)
    {
        if (workMinutes <= 0)
            throw new ArgumentException("Work minutes must be positive.", nameof(workMinutes));
        if (shortBreakMinutes <= 0)
            throw new ArgumentException("Short break minutes must be positive.", nameof(shortBreakMinutes));
        if (cyclesBeforeLongBreak <= 0)
            throw new ArgumentException("Cycles before long break must be positive.", nameof(cyclesBeforeLongBreak));
        if (longBreakMinutes <= 0)
            throw new ArgumentException("Long break minutes must be positive.", nameof(longBreakMinutes));

        WorkMinutes = workMinutes;
        ShortBreakMinutes = shortBreakMinutes;
        CyclesBeforeLongBreak = cyclesBeforeLongBreak;
        LongBreakMinutes = longBreakMinutes;
    }

    /// <summary>
    /// Lays this rhythm over a session of <paramref name="runLength"/>: work, short
    /// break, ... long break every CyclesBeforeLongBreak cycles, repeating. A break is
    /// only included if it can run to completion AND leaves some work time after it —
    /// one that would be cut short, or that ends exactly when the session does, is
    /// skipped and the schedule simply ends there. This is the one place that rule
    /// is expressed.
    /// </summary>
    public BreakSchedule ScheduleFor(TimeSpan runLength)
    {
        var breaks = new List<SessionBreak>();
        var work = TimeSpan.FromMinutes(WorkMinutes);
        var elapsed = TimeSpan.Zero;
        var cyclesCompleted = 0;

        while (true)
        {
            var workEnd = elapsed + work;
            if (workEnd >= runLength) break;

            cyclesCompleted++;
            var isLong = cyclesCompleted % CyclesBeforeLongBreak == 0;
            var breakEnd = workEnd + TimeSpan.FromMinutes(isLong ? LongBreakMinutes : ShortBreakMinutes);
            if (breakEnd >= runLength) break;

            breaks.Add(new SessionBreak(
                isLong ? BreakKind.LongBreak : BreakKind.ShortBreak, workEnd, breakEnd));
            elapsed = breakEnd;
            if (isLong) cyclesCompleted = 0;
        }

        return new BreakSchedule(breaks);
    }
}
