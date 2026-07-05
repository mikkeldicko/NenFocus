using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public sealed class FocusBlockOutcomeClassifier : IFocusBlockOutcomeClassifier
{
    public FocusBlockOutcome? Classify(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime asOf)
    {
        if (ritualCompletedAt is { } completedAt)
        {
            if (completedAt <= scheduledStart) return FocusBlockOutcome.CompletedOnTime;
            if (completedAt < scheduledEnd) return FocusBlockOutcome.StartedLate;
            return FocusBlockOutcome.Missed;
        }

        return asOf >= scheduledEnd ? FocusBlockOutcome.Missed : null;
    }
}
