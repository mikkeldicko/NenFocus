using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

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

    public bool IsLateStart(TimeSlot scheduled, Moment momentToCheck)
        => momentToCheck > scheduled.Start + TimeSpan.FromMinutes(GracePeriodMinutes);

    public FocusBlockOutcome? Classify(FocusBlock block, Moment asOf)
        => Classify(block.Scheduled, block.RitualCompletedAt, block.SessionEndedAt, block.HasEnded(asOf));

    // Private: callers hand over the block, never loose dates, so "when did the session end"
    // can only ever come from the block's own stamp (a faithful copy of Session.EndedAt), and
    // "has its window closed" from the block's own slot — the same answer IsActive and
    // IsUnresolved give (Gap 88 parity). "Before the scheduled end" is the slot's own rule
    // (!HasEnded), not a raw comparison.
    private FocusBlockOutcome? Classify(
        TimeSlot scheduled,
        Moment? ritualCompletedAt,
        Moment? sessionEndedAt,
        bool scheduleHasEnded)
    {
        if (ritualCompletedAt is { } completedAt)
        {
            if (sessionEndedAt is { } endedAt && !scheduled.HasEnded(endedAt)) return FocusBlockOutcome.EndedEarly;
            if (!IsLateStart(scheduled, completedAt)) return FocusBlockOutcome.CompletedOnTime;
            if (!scheduled.HasEnded(completedAt)) return FocusBlockOutcome.StartedLate;
            return FocusBlockOutcome.Missed;
        }

        return scheduleHasEnded ? FocusBlockOutcome.Missed : null;
    }

    public double CreditedFocusMinutes(
        TimeSlot scheduled,
        Moment? ritualCompletedAt,
        Moment? sessionEndedAt,
        double actualElapsedMinutes)
        => CreditedWindow(scheduled, ritualCompletedAt, sessionEndedAt, actualElapsedMinutes)?.Length.TotalMinutes ?? 0;

    /// <summary>
    /// Floored to whole minutes as the final step — the single source every consumer of
    /// "how much did this block earn" reads from, whether they read the duration through
    /// CreditedFocusMinutes or split this window across days (GetNenContractCompletedFocusHoursQueryHandler
    /// and GetNenContractBonusFocusHoursQueryHandler both do). Flooring only the returned
    /// end here, never the raw RitualCompletedAt/SessionEndedAt stamps a caller might
    /// separately read for genuinely different purposes (e.g. GetPlannedBlocksForDayQueryHandler's
    /// tile trimming reads FocusBlock.SessionEndedAt directly, not through this method, and
    /// stays exact — that's a display concern, not a credit one).
    /// </summary>
    public TimeSlot? CreditedWindow(
        TimeSlot scheduled,
        Moment? ritualCompletedAt,
        Moment? sessionEndedAt,
        double actualElapsedMinutes)
    {
        if (ComputeRawWindow(scheduled, ritualCompletedAt, sessionEndedAt, actualElapsedMinutes) is not { } raw)
            return null;

        var flooredMinutes = Math.Floor(raw.Length.TotalMinutes);
        return flooredMinutes > 0 ? new TimeSlot(raw.Start, raw.Start + TimeSpan.FromMinutes(flooredMinutes)) : null;
    }

    private TimeSlot? ComputeRawWindow(
        TimeSlot scheduled,
        Moment? ritualCompletedAt,
        Moment? sessionEndedAt,
        double actualElapsedMinutes)
    {
        if (ritualCompletedAt is not { } completedAt)
            return null;

        // The grace period forgives a late start unconditionally, regardless of what happens
        // to the session afterward — blocking enforcement is already active on the user's
        // computer from the block's own scheduled start onward (PolicyEngine.ComputeSlice
        // keys off the block's slot, never RitualCompletedAt), so crediting
        // that setup window is honestly counting real enforcement time, not inventing work
        // that didn't happen. A start beyond grace gets no such allowance — credit starts
        // from the moment the Ritual actually completed.
        var effectiveStart = IsLateStart(scheduled, completedAt) ? completedAt : scheduled.Start;

        // A genuine early end always counts exactly what was delivered, however small the
        // shortfall — no forgiveness (Gap 45 removed): every shortfall is now recoverable
        // through Cover Deficit instead of being silently written off.
        if (sessionEndedAt is { } endedAt && !scheduled.HasEnded(endedAt))
            return endedAt > effectiveStart ? new TimeSlot(effectiveStart, endedAt) : null;

        // Ran to completion — or was force-ended at/after the scheduled end (e.g. via the debug
        // clock) — credit never exceeds the block's own scheduled window.
        if (sessionEndedAt.HasValue)
            return scheduled.End > effectiveStart ? new TimeSlot(effectiveStart, scheduled.End) : null;

        // Still running: credit accrues progressively with real elapsed time, bounded by the
        // block's own scheduled end, so the Planner badges/banner move live while active.
        var liveEnd = completedAt + TimeSpan.FromMinutes(actualElapsedMinutes);
        var cappedEnd = liveEnd < scheduled.End ? liveEnd : scheduled.End;
        return cappedEnd > effectiveStart ? new TimeSlot(effectiveStart, cappedEnd) : null;
    }
}
