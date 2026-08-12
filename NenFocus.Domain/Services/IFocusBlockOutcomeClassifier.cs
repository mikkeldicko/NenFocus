using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public interface IFocusBlockOutcomeClassifier
{
    /// <summary>
    /// Classifies a Focus block's outcome from its scheduled window, ritual
    /// completion state, and (if the session ended before scheduledEnd) when the
    /// session ended. Returns null if no verdict has been reached yet — the
    /// block is still open (in progress or future) and could still be started
    /// late right up to its scheduled end. A ritual completed within the grace
    /// period after scheduledStart still counts as CompletedOnTime, unless the
    /// session ended early, which takes priority and produces EndedEarly instead.
    /// </summary>
    FocusBlockOutcome? Classify(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        DateTime asOf);

    /// <summary>
    /// Focus minutes to credit toward a Daily Floor for this block. Within the grace
    /// period, the full scheduled duration is credited regardless of actual elapsed
    /// time — the Ritual takes a moment, and a 13:00-15:00 block with ritual at 13:03
    /// still credits the full 2 hours. Outside the grace period (or missed), only the
    /// actual elapsed time is credited. A session that ended before scheduledEnd
    /// always credits only the actual elapsed time, regardless of grace period — the
    /// grace period forgives a late start, not an early end.
    /// </summary>
    double CreditedFocusMinutes(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        double actualElapsedMinutes);
}
