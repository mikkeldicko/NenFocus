using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// See IBreakScheduleCalculator for the full contract and rationale. Pure and
/// deterministic — no clock access, same inputs always produce the same
/// timeline, computed once rather than decided incrementally.
/// </summary>
public sealed class BreakScheduleCalculator : IBreakScheduleCalculator
{
    public IReadOnlyList<SessionPhase> ComputeSchedule(BreakTimer breakTimer, TimeSpan totalDuration)
    {
        var phases = new List<SessionPhase>();

        if (totalDuration <= TimeSpan.Zero)
            return phases;

        var workLength = TimeSpan.FromMinutes(breakTimer.WorkMinutes);
        var shortBreakLength = TimeSpan.FromMinutes(breakTimer.ShortBreakMinutes);
        var longBreakLength = TimeSpan.FromMinutes(breakTimer.LongBreakMinutes);

        var elapsed = TimeSpan.Zero;
        var cyclesCompleted = 0;

        while (elapsed < totalDuration)
        {
            var workEnd = elapsed + workLength;
            if (workEnd >= totalDuration)
            {
                // This work period doesn't fully fit (or fits exactly with nothing left
                // over) — it's the last phase, truncated to whatever time remains.
                phases.Add(new SessionPhase(SessionPhaseKind.Work, elapsed, totalDuration));
                break;
            }

            phases.Add(new SessionPhase(SessionPhaseKind.Work, elapsed, workEnd));
            elapsed = workEnd;
            cyclesCompleted++;

            var isLongBreak = cyclesCompleted % breakTimer.CyclesBeforeLongBreak == 0;
            var breakLength = isLongBreak ? longBreakLength : shortBreakLength;
            var breakEnd = elapsed + breakLength;

            // A break only belongs in the timeline if it can run to completion AND
            // leaves real Work time after it — a break that would be cut short, or
            // that ends exactly when the session does, is dead air. Skip it and fold
            // the remaining time into the Work phase that precedes it instead of
            // leaving a gap in the timeline.
            if (breakEnd >= totalDuration)
            {
                var lastWork = phases[^1];
                phases[^1] = lastWork with { End = totalDuration };
                break;
            }

            phases.Add(new SessionPhase(
                isLongBreak ? SessionPhaseKind.LongBreak : SessionPhaseKind.ShortBreak,
                elapsed,
                breakEnd));
            elapsed = breakEnd;

            if (isLongBreak)
                cyclesCompleted = 0;
        }

        return phases;
    }
}
