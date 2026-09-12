using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// One entry in the phase timeline IBreakScheduleCalculator produces — Kind plus
/// its [Start, End) offset from the session's own start. The single fact every
/// consumer of the timeline reads: ActiveSessionMonitor's automatic work/break
/// transitions, the Main Page progress bar's break markers, and Hatsu's
/// "does this preset make sense for this duration" filtering all read the same
/// list rather than each re-deriving cycle math independently.
/// </summary>
public sealed record SessionPhase(SessionPhaseKind Kind, TimeSpan Start, TimeSpan End);
