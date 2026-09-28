using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public sealed class FocusBlock : PlannedBlock
{
    /// <summary>
    /// A Focus block cannot exceed this many hours — Background blocks have no such
    /// limit. Focus-only by design, mirroring the already-established Focus-only Work
    /// Hours exception. Lives here (not a validator) because it's a pure fact about
    /// Start/End, the same justification already used for the entity's own end &gt; start
    /// guard — no repository lookup needed, so nothing pushes it out of Domain.
    /// </summary>
    public const int MaxDurationHours = 12;

    /// <summary>Null means the Ritual was never completed for this block.</summary>
    public DateTime? RitualCompletedAt { get; private set; }

    /// <summary>
    /// When this block's session ended, early or on time — a faithful copy of the session's
    /// own EndedAt, written in the same save; null while no session has ended (Gap 86). An
    /// early end is simply one before End: every reader compares it to End itself.
    /// </summary>
    public DateTime? SessionEndedAt { get; private set; }

    private FocusBlock() { }

    public override BlockPresetType PresetType => BlockPresetType.Focus;

    /// <summary>Whether this block redeems other blocks' deficits — it carries redemption entries.</summary>
    public bool IsRedemption => RedemptionEntries.Count > 0;

    /// <summary>
    /// Window closed, Ritual never completed, not cancelled — what the missed-block sweep looks
    /// for. <c>IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync</c> mirrors this in SQL for
    /// speed; a repository test checks the two agree.
    /// </summary>
    public bool IsUnresolved(DateTime asOf)
        => HasEnded(asOf) && RitualCompletedAt is null && CancelledAt is null;

    /// <summary>Once the Ritual is done, a session owns this block's ending and announces it.</summary>
    protected override bool AnnouncesOwnEnd => RitualCompletedAt is null;

    public static FocusBlock Create(
        Guid id,
        Guid blockPresetId,
        DateTime start,
        DateTime end,
        string? title = null,
        Guid? contractId = null,
        IReadOnlyList<RedemptionEntry>? redemptionEntries = null)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");
        ValidateDuration(start, end);

        return new FocusBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Start = start,
            End = end,
            Title = title,
            ContractId = contractId,
            RedemptionEntries = redemptionEntries ?? Array.Empty<RedemptionEntry>()
        };
    }

    public override void Reschedule(DateTime start, DateTime end)
    {
        ValidateDuration(start, end);
        base.Reschedule(start, end);
    }

    private static void ValidateDuration(DateTime start, DateTime end)
    {
        if (end - start > TimeSpan.FromHours(MaxDurationHours))
            throw new ArgumentException($"A Focus block cannot exceed {MaxDurationHours} hours.");
    }

    public void MarkRitualCompleted(DateTime completedAt)
    {
        if (RitualCompletedAt.HasValue)
            throw new InvalidOperationException(
                "Ritual has already been marked complete for this block.");

        RitualCompletedAt = completedAt;
    }

    public void MarkSessionEnded(DateTime endedAt)
    {
        if (SessionEndedAt.HasValue)
            throw new InvalidOperationException(
                "Session end has already been marked for this block.");

        SessionEndedAt = endedAt;
    }

    /// <summary>
    /// True when this block is live at <paramref name="at"/> and its Ritual hasn't been
    /// completed yet — the one definition of "the user should be pushed into the Ritual
    /// for this block." Replaces ActivePolicySlice.FocusSessionCompleted, which answered
    /// the same question indirectly (Gap 62).
    /// </summary>
    public bool IsAwaitingRitual(DateTime at)
        => RitualCompletedAt is null && IsActive(at);

    /// <summary>
    /// Tail freed once a session ended early; whole window freed once the block is genuinely
    /// Missed (ritual never completed and its scheduled End has already passed) — mirrors
    /// <see cref="Services.IFocusBlockOutcomeClassifier.Classify"/>'s own Missed condition so
    /// the two can never disagree about when a block is truly vacated. Still-pending or
    /// fully-used blocks keep the whole window reserved. Adds to the base reasons
    /// (cancellation): whichever reason came first wins.
    /// </summary>
    public override DateTime EffectiveEnd(DateTime asOf)
    {
        if (RitualCompletedAt is null && asOf >= End) return Start;

        var end = base.EffectiveEnd(asOf);
        return SessionEndedAt is { } endedEarly && endedEarly < end ? endedEarly : end;
    }

    /// <summary>
    /// Replaces which missed/late blocks this redemption block covers. A plain setter, same
    /// as AssignPreset/AssignContract — same-preset, uncovered-deficit, and same-day
    /// constraints are Application-layer concerns, not enforced here.
    /// </summary>
    public void AssignRedemptionEntries(IReadOnlyList<RedemptionEntry> entries)
    {
        RedemptionEntries = entries;
    }

    /// <summary>
    /// A redemption block can never claim more scheduled time than it actually redeems —
    /// covering less than what's owed is fine (the rest stays outstanding for a later
    /// block), but claiming more would let unaccounted "extra" time hide inside a block
    /// whose REDEMPTION badge implies its whole span is accounted-for deficit coverage.
    /// Pure and self-contained (only this block's own Start/End/RedemptionEntries), so it
    /// lives here rather than in an Application handler — the same justification already
    /// used for MaxDurationHours/ValidateDuration on this entity.
    ///
    /// Caps to the *exact* covered amount — not rounded to a whole minute. This looks like
    /// it should collide with MODULE_STANDARDS.md's "round up before persisting a fraction
    /// into a timestamp" rule, but it doesn't: that rule protects a value that gets read
    /// back out of *this* timestamp later and compared against an "at least X" threshold
    /// (e.g. a suggested tile's own size, checked against the deficit it must fully cover).
    /// `entries` here is the opposite direction — each `RedemptionEntry.MinutesCovered` is
    /// already a precise double, stored as-is and never re-derived from this block's own
    /// End, so nothing downstream re-reads this timestamp to recover it. Previously this
    /// floored to a whole minute, which silently threw away up to 59 seconds of real
    /// coverage on every redemption whose deficit wasn't a clean whole number (i.e. nearly
    /// all of them) and, when the remaining coverage was itself under a minute, floored to
    /// zero — producing an End equal to Start and crashing FocusBlock.Create. Exact
    /// arithmetic can never do that: a RedemptionEntry can never be zero or negative, so
    /// AddMinutes(coveredMinutes) can never equal `start`.
    /// </summary>
    public static DateTime CapEndToRedemptionCoverage(DateTime start, DateTime requestedEnd, IReadOnlyList<RedemptionEntry> entries)
    {
        if (entries.Count == 0) return requestedEnd;

        var coveredMinutes = entries.Sum(e => e.MinutesCovered);
        var requestedMinutes = (requestedEnd - start).TotalMinutes;
        if (coveredMinutes >= requestedMinutes) return requestedEnd;

        return start.AddMinutes(coveredMinutes);
    }

    /// <summary>
    /// True when CapEndToRedemptionCoverage's shortening is large enough to be worth
    /// surfacing to the user, as opposed to merely trimming off the sub-minute safety
    /// margin every Cover Deficit suggestion is deliberately over-sized by (see
    /// GetSuggestedRedemptionPlacementsQueryHandler's own whole-minute rounding, required by
    /// the same MODULE_STANDARDS.md rule referenced above). Without this, "shortened to
    /// match" would fire on that deliberate safety margin every ordinary Cover Deficit save.
    /// Shares FocusBlockOutcomeClassifier.MinimumMeaningfulDeficitMinutes purely as a
    /// UI-notice threshold now — the deficit ledger's own write-off and CreditedWindow's
    /// sub-minute-shortfall forgiveness this constant used to also gate were both removed
    /// (Gap 44/45): every deficit is now whole minutes and fully recoverable, no write-off
    /// anywhere. This is the one remaining consumer of the constant.
    /// </summary>
    public static bool WasRedemptionCoverageMeaningfullyCapped(DateTime requestedEnd, DateTime cappedEnd)
        => (requestedEnd - cappedEnd).TotalMinutes >= FocusBlockOutcomeClassifier.MinimumMeaningfulDeficitMinutes;

    /// <summary>
    /// True when this block's own outcome represents an unredeemed shortfall of its own —
    /// Missed, StartedLate, or EndedEarly — and it isn't itself already serving as a
    /// redemption for another block. A block actively covering someone else's deficit never
    /// becomes a deficit source in its own right (e.g. a Ritual that ran a few minutes past
    /// its own grace period, StartedLate against its own schedule): the commitment it exists
    /// to redeem is what's tracked, not a second one layered on top of it. Takes the outcome
    /// as a parameter rather than reclassifying — IFocusBlockOutcomeClassifier.Classify needs
    /// "now" and session data the caller already has, and the two must never disagree about
    /// what the outcome was.
    /// </summary>
    public bool CanContributeOwnDeficit(FocusBlockOutcome? outcome) =>
        outcome is FocusBlockOutcome.Missed or FocusBlockOutcome.StartedLate or FocusBlockOutcome.EndedEarly
        && !IsRedemption;

    /// <summary>
    /// How much of this block's own scheduled duration was never delivered — this block's
    /// side of the deficit ledger, independent of how many other blocks have since attempted
    /// to redeem it. Fixed forever once this block has concluded (RitualCompletedAt/
    /// SessionEndedAt are set-once fields), so callers can treat it as a stable, cacheable
    /// fact rather than something that drifts as later redemption attempts come and go.
    /// Pure and self-contained (this block's own Start/End plus a credited-minutes figure the
    /// caller already resolved via IFocusBlockOutcomeClassifier), the same justification
    /// already used for CanContributeOwnDeficit/WasRedemptionCoverageMeaningfullyCapped — the
    /// rule belongs with the data it's about, not re-derived inline by every caller that needs
    /// it (GetNenContractDeficitsQueryHandler and GetNenContractsForDateRangeQueryHandler both
    /// need this exact number and must never be allowed to disagree about what it means).
    /// </summary>
    public double RawDeficitMinutes(double creditedMinutes) =>
        Math.Max(0, GetDuration().TotalMinutes - creditedMinutes);
}
