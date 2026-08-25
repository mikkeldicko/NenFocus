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
        var window = CreditedWindow(scheduledStart, scheduledEnd, ritualCompletedAt, sessionEndedAt, actualElapsedMinutes);
        return window is { } w ? (w.End - w.Start).TotalMinutes : 0;
    }

    public (DateTime Start, DateTime End)? CreditedWindow(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        double actualElapsedMinutes)
    {
        if (sessionEndedAt is { } endedEarly && endedEarly < scheduledEnd)
            return ritualCompletedAt is { } startedAt ? (startedAt, startedAt.AddMinutes(actualElapsedMinutes)) : null;

        if (ritualCompletedAt is { } completedAt)
        {
            if (!IsLateStart(scheduledStart, completedAt))
                return (scheduledStart, scheduledEnd);

            // Started late: credit is bounded by the block's own scheduled end — a session
            // that ran (or was force-ended, e.g. via the debug clock) past scheduledEnd earns
            // no more credit than a full-length, on-time completion would have.
            var cappedMinutes = Math.Min(actualElapsedMinutes, (scheduledEnd - completedAt).TotalMinutes);
            return cappedMinutes > 0 ? (completedAt, completedAt.AddMinutes(cappedMinutes)) : null;
        }

        return null;
    }
}
