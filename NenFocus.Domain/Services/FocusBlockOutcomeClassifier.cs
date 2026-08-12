using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public sealed class FocusBlockOutcomeClassifier : IFocusBlockOutcomeClassifier
{
    /// <summary>
    /// A ritual completed within this many minutes after scheduledStart still counts as
    /// on time — the Ritual takes a moment, and penalizing a 13:03 completion for a
    /// 13:00 block is noise. Shared by Classify and CreditedFocusMinutes so the two can
    /// never disagree about what "within grace" means.
    /// </summary>
    public const int GracePeriodMinutes = 5;

    public FocusBlockOutcome? Classify(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        DateTime asOf)
    {
        if (ritualCompletedAt is { } completedAt)
        {
            if (sessionEndedAt is { } endedEarly && endedEarly < scheduledEnd) return FocusBlockOutcome.EndedEarly;
            if (completedAt <= scheduledStart.AddMinutes(GracePeriodMinutes)) return FocusBlockOutcome.CompletedOnTime;
            if (completedAt < scheduledEnd) return FocusBlockOutcome.StartedLate;
            return FocusBlockOutcome.Missed;
        }

        return asOf >= scheduledEnd ? FocusBlockOutcome.Missed : null;
    }

    public double CreditedFocusMinutes(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        double actualElapsedMinutes)
    {
        if (sessionEndedAt is { } endedEarly && endedEarly < scheduledEnd)
            return actualElapsedMinutes;

        if (ritualCompletedAt is { } completedAt && completedAt <= scheduledStart.AddMinutes(GracePeriodMinutes))
            return (scheduledEnd - scheduledStart).TotalMinutes;

        return actualElapsedMinutes;
    }
}
