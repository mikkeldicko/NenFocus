using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The Deep Module at the center of the BreakTimerPreset feature. Given a
/// cadence and a total session length, produces the complete, ordered phase
/// timeline for the whole session — deterministic and pure, computed once, not
/// decided incrementally as the session runs.
///
/// Every consumer reads this same timeline instead of separately tracking or
/// re-deriving cycle state:
///   - ActiveSessionMonitor walks it to auto-trigger work-to-break and
///     break-to-work transitions (replacing manual "Take Break", now retired).
///   - The Main Page progress bar renders a marker at each break's Start,
///     styled distinctly for LongBreak vs ShortBreak.
///   - Hatsu filters out a preset whose first break either can't finish, or
///     would leave no real Work time afterward, for the duration currently in
///     play (user-picked for a Spontaneous session, block-derived for a
///     Planned one).
///
/// The cadence loops for as long as totalDuration allows. A break is only ever
/// included if it can run to completion within totalDuration AND leaves at
/// least some Work time after it — a break that would be cut short, or that
/// ends exactly at totalDuration with nothing left to work on, is skipped
/// entirely, and the timeline simply ends on the surrounding Work phase
/// instead. This is the one place that rule is expressed; nothing else
/// re-derives it.
/// </summary>
public interface IBreakScheduleCalculator
{
    IReadOnlyList<SessionPhase> ComputeSchedule(BreakTimer breakTimer, TimeSpan totalDuration);
}
