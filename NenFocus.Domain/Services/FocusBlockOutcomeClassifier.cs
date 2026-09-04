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

    /// <summary>
    /// A shortfall from a block's own scheduled end this small (in minutes) is forgiven
    /// outright — CreditedWindow credits the block as if it had run the rest of the way,
    /// the same forgiveness GracePeriodMinutes already gives a late start, mirrored to the
    /// other end but at a far smaller grain (this app doesn't excuse several real minutes of
    /// quitting early the way it excuses a few minutes of Ritual setup — only a handful of
    /// seconds). Classify is deliberately untouched — the block's own outcome still honestly
    /// reads EndedEarly; only how much it counts toward Daily Floor/Contract Floor changes.
    /// Shared with FocusBlock.WasRedemptionCoverageMeaningfullyCapped and
    /// GetNenContractDeficitsQueryHandler's own write-off check — one number, one meaning,
    /// everywhere "is this shortfall worth caring about" gets asked (Gap 44, Gap 45).
    /// </summary>
    public const double MinimumMeaningfulDeficitMinutes = 1;

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
        if (ritualCompletedAt is not { } completedAt)
            return null;

        var endedEarlyShortfallMinutes = sessionEndedAt is { } endedAt && endedAt < scheduledEnd
            ? (scheduledEnd - endedAt).TotalMinutes
            : (double?)null;

        // A genuine early end — more than a sliver short of the block's own scheduled end —
        // credits only what was actually worked, regardless of how the block started; the
        // grace period never covers an early end (Gap 22), except the sub-minute write-off
        // below (Gap 45).
        if (endedEarlyShortfallMinutes is { } shortfall && shortfall > MinimumMeaningfulDeficitMinutes)
            return (completedAt, completedAt.AddMinutes(actualElapsedMinutes));

        // A session that has actually ended — not still running — and started within the
        // grace period credits the block's full scheduled duration: the grace period's whole
        // purpose is to not dock a few minutes of Ritual setup once the block has genuinely
        // run its course (sessionEndedAt present, and not an unforgiven early end — already
        // ruled out above). A live, in-progress query (sessionEndedAt still null — the
        // session hasn't ended yet) is a different question: "how much has this block earned
        // so far," which must accrue progressively with real elapsed time rather than jump to
        // the full duration the instant the Ritual completes, so the Planner badges/banner
        // move live while a block is still active.
        if (sessionEndedAt.HasValue && !IsLateStart(scheduledStart, completedAt))
            return (scheduledStart, scheduledEnd);

        // Started late, or still running: credit is bounded by the block's own scheduled end —
        // a session that ran (or was force-ended, e.g. via the debug clock) past scheduledEnd
        // earns no more credit than a full-length completion would have. A forgiven sub-minute
        // early-end shortfall (endedEarlyShortfallMinutes above) credits the full completedAt-
        // to-scheduled-end span rather than the fractionally-shorter real elapsed time.
        var minutesToCredit = endedEarlyShortfallMinutes.HasValue
            ? (scheduledEnd - completedAt).TotalMinutes
            : actualElapsedMinutes;
        var cappedMinutes = Math.Min(minutesToCredit, (scheduledEnd - completedAt).TotalMinutes);
        return cappedMinutes > 0 ? (completedAt, completedAt.AddMinutes(cappedMinutes)) : null;
    }
}
