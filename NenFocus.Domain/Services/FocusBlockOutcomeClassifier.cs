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

    public bool IsLateStart(DateTime scheduledStart, DateTime momentToCheck)
        => momentToCheck > scheduledStart.AddMinutes(GracePeriodMinutes);

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
            if (!IsLateStart(scheduledStart, completedAt)) return FocusBlockOutcome.CompletedOnTime;
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

        if (ritualCompletedAt is { } completedAt)
        {
            if (!IsLateStart(scheduledStart, completedAt))
                return (scheduledEnd - scheduledStart).TotalMinutes;

            // Started late: credit is bounded by the block's own scheduled end — a session
            // that ran (or was force-ended, e.g. via the debug clock) past scheduledEnd earns
            // no more credit than a full-length, on-time completion would have.
            return Math.Min(actualElapsedMinutes, (scheduledEnd - completedAt).TotalMinutes);
        }

        return actualElapsedMinutes;
    }
}
