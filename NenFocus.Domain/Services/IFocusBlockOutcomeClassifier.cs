using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public interface IFocusBlockOutcomeClassifier
{
    /// <summary>
    /// Classifies a Focus block's outcome from its scheduled window and ritual
    /// completion state. Returns null if no verdict has been reached yet — the
    /// block is still open (in progress or future) and could still be started
    /// late right up to its scheduled end.
    /// </summary>
    FocusBlockOutcome? Classify(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime asOf);
}
