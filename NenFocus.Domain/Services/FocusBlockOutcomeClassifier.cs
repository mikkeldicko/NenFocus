using NenFocus.Domain.Entities;
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
    /// No longer a credited-time write-off (Gap 44/45 removed — every shortfall now counts,
    /// however small, and is always recoverable through Cover Deficit; nothing is silently
    /// forgiven any more). Still used by FocusBlock.WasRedemptionCoverageMeaningfullyCapped,
    /// a genuinely separate, narrower question: whether a redemption block's save-time
    /// shortening from its requested End is worth surfacing as a UI notice, not whether the
    /// shortfall itself counts.
    /// </summary>
    public const double MinimumMeaningfulDeficitMinutes = 1;

    public bool IsLateStart(DateTime scheduledStart, DateTime momentToCheck)
        => momentToCheck > scheduledStart.AddMinutes(GracePeriodMinutes);

    public FocusBlockOutcome? Classify(FocusBlock block, DateTime asOf)
        => Classify(block.Start, block.End, block.RitualCompletedAt, block.SessionEndedAt, asOf);

    // Private: callers hand over the block, never loose dates, so "when did the session end"
    // can only ever come from the block's own stamp (a faithful copy of Session.EndedAt).
    private FocusBlockOutcome? Classify(
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

    /// <summary>
    /// Floored to whole minutes as the final step — the single source every consumer of
    /// "how much did this block earn" reads from, whether they read the duration through
    /// CreditedFocusMinutes or use this window's Start/End directly (GetNenContractCompletedFocusHoursQueryHandler
    /// and GetNenContractBonusFocusHoursQueryHandler both do). Flooring only the returned
    /// End here, never the raw RitualCompletedAt/SessionEndedAt timestamps a caller might
    /// separately read for genuinely different purposes (e.g. GetPlannedBlocksForDayQueryHandler's
    /// tile trimming reads FocusBlock.SessionEndedAt directly, not through this method, and
    /// stays exact — that's a display concern, not a credit one).
    /// </summary>
    public (DateTime Start, DateTime End)? CreditedWindow(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        double actualElapsedMinutes)
    {
        var raw = ComputeRawWindow(scheduledStart, scheduledEnd, ritualCompletedAt, sessionEndedAt, actualElapsedMinutes);
        if (raw is not { } w) return null;

        var flooredMinutes = Math.Floor((w.End - w.Start).TotalMinutes);
        return flooredMinutes > 0 ? (w.Start, w.Start.AddMinutes(flooredMinutes)) : null;
    }

    private (DateTime Start, DateTime End)? ComputeRawWindow(
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime? ritualCompletedAt,
        DateTime? sessionEndedAt,
        double actualElapsedMinutes)
    {
        if (ritualCompletedAt is not { } completedAt)
            return null;

        // The grace period forgives a late start unconditionally, regardless of what happens
        // to the session afterward — blocking enforcement is already active on the user's
        // computer from the block's own scheduled Start onward (PolicyEngine.ComputeSlice
        // keys entirely off PlannedBlock.Start/End, never RitualCompletedAt), so crediting
        // that setup window is honestly counting real enforcement time, not inventing work
        // that didn't happen. A start beyond grace gets no such allowance — credit starts
        // from the moment the Ritual actually completed.
        var effectiveStart = IsLateStart(scheduledStart, completedAt) ? completedAt : scheduledStart;

        // A genuine early end always counts exactly what was delivered, however small the
        // shortfall — no forgiveness (Gap 45 removed): every shortfall is now recoverable
        // through Cover Deficit instead of being silently written off.
        if (sessionEndedAt is { } endedEarly && endedEarly < scheduledEnd)
            return endedEarly > effectiveStart ? (effectiveStart, endedEarly) : null;

        // Ran to completion — or was force-ended at/after scheduledEnd (e.g. via the debug
        // clock) — credit never exceeds the block's own scheduled window.
        if (sessionEndedAt.HasValue)
            return (effectiveStart, scheduledEnd);

        // Still running: credit accrues progressively with real elapsed time, bounded by the
        // block's own scheduled end, so the Planner badges/banner move live while active.
        var liveEnd = completedAt.AddMinutes(actualElapsedMinutes);
        var cappedEnd = liveEnd < scheduledEnd ? liveEnd : scheduledEnd;
        return cappedEnd > effectiveStart ? (effectiveStart, cappedEnd) : null;
    }
}
