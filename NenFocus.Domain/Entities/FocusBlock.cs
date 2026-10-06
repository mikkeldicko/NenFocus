using NenFocus.Domain.Enums;
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

    /// <summary>
    /// A Ritual completed within this many minutes after the scheduled start still counts as on
    /// time — the Ritual takes a moment, and blocking was already running from the start.
    /// </summary>
    public const int GracePeriodMinutes = 5;

    /// <summary>
    /// How short a redemption block's save-time trim must be before the user is told about it
    /// (<see cref="WasRedemptionCoverageMeaningfullyCapped"/>). Not a write-off of any kind.
    /// </summary>
    public const double MinimumMeaningfulDeficitMinutes = 1;

    /// <summary>Null means the Ritual was never completed for this block.</summary>
    public Moment? RitualCompletedAt { get; private set; }

    /// <summary>
    /// When this block's session ended, early or on time — a faithful copy of the session's
    /// own EndedAt, written in the same save; null while no session has ended (Gap 86). An
    /// early end is simply one before End: every reader compares it to End itself.
    /// </summary>
    public Moment? SessionEndedAt { get; private set; }

    private FocusBlock() { }

    public override BlockPresetType PresetType => BlockPresetType.Focus;

    /// <summary>
    /// Whether this block was placed to redeem other blocks' deficits — its role in its contract,
    /// set when it was made (Gap 97). The entries only say what it covers, and may drop to none.
    /// </summary>
    public bool IsRedemption => Membership is { IsRedemption: true };

    /// <summary>Drafted and sealed as part of its contract — the hours the contract was sealed on.</summary>
    public bool IsCommitted => Membership is { IsCommitted: true };

    /// <summary>Placed on a contract without being drafted or redeeming: its hours are Bonus hours, never floor hours.</summary>
    public bool IsBonus => Membership is { IsBonus: true };

    /// <summary>Whether this block's hours count toward its contract's floor — Committed and Redemption do.</summary>
    public bool CountsTowardFloor => Membership is { CountsTowardFloor: true };

    /// <summary>
    /// Window closed, Ritual never completed, not cancelled — what the missed-block sweep looks
    /// for. <c>IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync</c> mirrors this in SQL for
    /// speed; a repository test checks the two agree.
    /// </summary>
    public bool IsUnresolved(Moment asOf)
        => HasEnded(asOf) && RitualCompletedAt is null && CancelledAt is null;

    /// <summary>
    /// Past the grace period at <paramref name="moment"/> — the one lateness rule, asked with the
    /// Ritual stamp (<see cref="StartedLate"/>) and with "now" (the Ritual's late notice).
    /// </summary>
    public bool IsLateAt(Moment moment) => moment > Scheduled.Start + TimeSpan.FromMinutes(GracePeriodMinutes);

    /// <summary>The Ritual was completed past the grace period, but before the block's end.</summary>
    public bool StartedLate =>
        RitualCompletedAt is { } ritualCompletedAt && IsLateAt(ritualCompletedAt) && !Scheduled.HasEnded(ritualCompletedAt);

    /// <summary>The session ended before the scheduled end.</summary>
    public bool EndedEarly => SessionEndedAt is { } sessionEndedAt && !Scheduled.HasEnded(sessionEndedAt);

    /// <summary>
    /// How the block resolved, or null while it could still be started (before its end, Ritual
    /// not done). One verdict per block, in this priority: Missed, EndedEarly, StartedLate,
    /// CompletedOnTime — <see cref="StartedLate"/> and <see cref="EndedEarly"/> can both be true.
    /// </summary>
    public FocusBlockOutcome? Outcome(Moment asOf)
    {
        if (RitualCompletedAt is not { } ritualCompletedAt)
            return HasEnded(asOf) ? FocusBlockOutcome.Missed : null;
        if (Scheduled.HasEnded(ritualCompletedAt)) return FocusBlockOutcome.Missed;
        if (EndedEarly) return FocusBlockOutcome.EndedEarly;
        return StartedLate ? FocusBlockOutcome.StartedLate : FocusBlockOutcome.CompletedOnTime;
    }

    /// <summary>
    /// The whole span the block is worked over, from its stamps alone: from the scheduled start
    /// when the Ritual came within grace (blocking was already running), else from the Ritual;
    /// to the session's end when it ended early, else the scheduled end (still planned while the
    /// session runs). Null when the Ritual wasn't done, or nothing is left of the span. The grace
    /// and early-end rules live only here.
    /// </summary>
    public TimeSlot? WorkSlot()
    {
        if (RitualCompletedAt is not { } ritualCompletedAt) return null;

        var start = IsLateAt(ritualCompletedAt) ? ritualCompletedAt : Scheduled.Start;
        var end = EndedEarly ? SessionEndedAt!.Value : Scheduled.End;
        return TimeSlot.TryCreate(start, end);
    }

    /// <summary>
    /// The Focus time this block has earned by <paramref name="asOf"/>: <see cref="WorkSlot"/>
    /// cut at <paramref name="asOf"/>, rounded down to whole minutes — rounding is credit's only
    /// rule of its own. Null when nothing is earned.
    /// </summary>
    public TimeSlot? CreditedSlot(Moment asOf)
    {
        if (WorkSlot() is not { } worked) return null;

        var end = asOf < worked.End ? asOf : worked.End;
        var wholeMinutes = Math.Floor((end - worked.Start).TotalMinutes);
        return wholeMinutes > 0 ? worked.Until(worked.Start + TimeSpan.FromMinutes(wholeMinutes)) : null;
    }

    /// <summary>
    /// The block's story has concluded: its session ended, or it was missed. A session still
    /// running isn't final — it could still end early (Redemption Rule 18).
    /// </summary>
    public override bool IsFinal(Moment asOf)
        => SessionEndedAt.HasValue || Outcome(asOf) == FocusBlockOutcome.Missed;

    /// <summary>
    /// What this block failed to deliver of its own schedule — its side of the deficit ledger,
    /// however many blocks have since tried to redeem it. Zero until final, zero when it was
    /// completed on time, and zero for a redemption block: a block covering someone else's
    /// deficit never becomes a deficit source itself (Rule 13).
    /// </summary>
    public TimeSpan OwnDeficit(Moment asOf)
    {
        if (IsRedemption || !IsFinal(asOf) || Outcome(asOf) == FocusBlockOutcome.CompletedOnTime)
            return TimeSpan.Zero;

        var credited = CreditedSlot(asOf)?.Length ?? TimeSpan.Zero;
        return credited < Scheduled.Length ? Scheduled.Length - credited : TimeSpan.Zero;
    }

    /// <summary>Once the Ritual is done, a session owns this block's ending and announces it.</summary>
    protected override bool AnnouncesOwnEnd => RitualCompletedAt is null;

    public static FocusBlock Create(
        Guid id,
        Guid blockPresetId,
        TimeSlot scheduled,
        string? title = null,
        ContractMembership? membership = null,
        IReadOnlyList<RedemptionEntry>? redemptionEntries = null)
    {
        ValidateDuration(scheduled);
        if (redemptionEntries is { Count: > 0 } && membership is not { IsRedemption: true })
            throw new ArgumentException("Only a redemption block can cover a deficit.", nameof(redemptionEntries));

        return new FocusBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Scheduled = scheduled,
            Title = title,
            Membership = membership,
            RedemptionEntries = redemptionEntries ?? Array.Empty<RedemptionEntry>()
        };
    }

    public override void Reschedule(TimeSlot scheduled)
    {
        ValidateDuration(scheduled);
        base.Reschedule(scheduled);
    }

    private static void ValidateDuration(TimeSlot slot)
    {
        if (slot.Length > TimeSpan.FromHours(MaxDurationHours))
            throw new ArgumentException($"A Focus block cannot exceed {MaxDurationHours} hours.");
    }

    public void MarkRitualCompleted(Moment completedAt)
    {
        if (RitualCompletedAt.HasValue)
            throw new InvalidOperationException(
                "Ritual has already been marked complete for this block.");

        RitualCompletedAt = completedAt;
    }

    /// <summary>
    /// Refuses a stamp the block's own answers (late, outcome, credit) can't interpret: no
    /// session can end on a block whose Ritual wasn't done, or before it was (Gap 95).
    /// </summary>
    public void MarkSessionEnded(Moment endedAt)
    {
        if (SessionEndedAt.HasValue)
            throw new InvalidOperationException(
                "Session end has already been marked for this block.");
        if (RitualCompletedAt is not { } ritualCompletedAt)
            throw new InvalidOperationException(
                "A session can't end on a block whose Ritual was never completed.");
        if (endedAt < ritualCompletedAt)
            throw new ArgumentException(
                "A session can't end before its Ritual was completed.", nameof(endedAt));

        SessionEndedAt = endedAt;
    }

    /// <summary>
    /// True when this block is live at <paramref name="at"/> and its Ritual hasn't been
    /// completed yet — the one definition of "the user should be pushed into the Ritual
    /// for this block." Replaces ActivePolicySlice.FocusSessionCompleted, which answered
    /// the same question indirectly (Gap 62).
    /// </summary>
    public bool IsAwaitingRitual(Moment at)
        => RitualCompletedAt is null && IsActive(at);

    /// <summary>
    /// Tail freed once a session ended early; whole window freed once the block is genuinely
    /// Missed (ritual never completed and its scheduled End has already passed) — mirrors
    /// <see cref="Outcome"/>'s own Missed condition so
    /// the two can never disagree about when a block is truly vacated. Still-pending or
    /// fully-used blocks keep the whole window reserved. Adds to the base reasons
    /// (cancellation): whichever reason came first wins.
    /// </summary>
    public override Moment EffectiveEnd(Moment asOf)
    {
        if (RitualCompletedAt is null && Scheduled.HasEnded(asOf)) return Scheduled.Start;

        var end = base.EffectiveEnd(asOf);
        return SessionEndedAt is { } endedEarly && endedEarly < end ? endedEarly : end;
    }

    /// <summary>
    /// Replaces which missed/late blocks this redemption block covers. Only a block whose role
    /// is Redemption may cover anything (Gap 97) — a Committed or Bonus block never claims a
    /// deficit. Same-preset, uncovered-deficit, and same-day constraints are Application-layer
    /// concerns, not enforced here.
    /// </summary>
    public void AssignRedemptionEntries(IReadOnlyList<RedemptionEntry> entries)
    {
        if (entries.Count > 0 && !IsRedemption)
            throw new InvalidOperationException("Only a redemption block can cover a deficit.");

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
    public static TimeSlot CapEndToRedemptionCoverage(TimeSlot requested, IReadOnlyList<RedemptionEntry> entries)
    {
        if (entries.Count == 0) return requested;

        var coveredMinutes = entries.Sum(e => e.MinutesCovered);
        if (coveredMinutes >= requested.Length.TotalMinutes) return requested;

        return requested.Until(requested.Start + TimeSpan.FromMinutes(coveredMinutes));
    }

    /// <summary>
    /// True when CapEndToRedemptionCoverage's shortening is large enough to be worth
    /// surfacing to the user, as opposed to merely trimming off the sub-minute safety
    /// margin every Cover Deficit suggestion is deliberately over-sized by (see
    /// GetSuggestedRedemptionPlacementsQueryHandler's own whole-minute rounding, required by
    /// the same MODULE_STANDARDS.md rule referenced above). Without this, "shortened to
    /// match" would fire on that deliberate safety margin every ordinary Cover Deficit save.
    /// A UI-notice threshold only (<see cref="MinimumMeaningfulDeficitMinutes"/>) — nothing is
    /// written off: every deficit is whole minutes and recoverable (Gaps 44, 45, 57).
    /// </summary>
    public static bool WasRedemptionCoverageMeaningfullyCapped(TimeSlot requested, TimeSlot capped)
        => (requested.Length - capped.Length).TotalMinutes >= MinimumMeaningfulDeficitMinutes;
}
