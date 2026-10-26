using System.Linq.Expressions;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public sealed class FocusBlock : Block
{
    /// <summary>
    /// A Focus block cannot exceed this many hours — Background blocks have no such
    /// limit. Focus-only by design, mirroring the already-established Focus-only Work
    /// Hours exception. Lives here (not a validator) because it's a pure fact about
    /// Start/End, the same justification already used for the entity's own end &gt; start
    /// guard — no repository lookup needed, so nothing pushes it out of Domain.
    /// </summary>
    public static TimeSpan MaxLength { get; } = TimeSpan.FromHours(12);

    /// <summary>Whether a Focus block may run this long — asked by the entity itself and by the
    /// validators that want a friendly refusal first, so the cap has one formula (Gap 89 Slice B).</summary>
    public static bool FitsMaxLength(TimeSlot slot) => slot.Length <= MaxLength;

    /// <summary>
    /// A Ritual completed within this many minutes after the scheduled start still counts as on
    /// time — the Ritual takes a moment, and blocking was already running from the start.
    /// </summary>
    public const int GracePeriodMinutes = 5;

    /// <summary>Null means the Ritual was never completed for this block.</summary>
    internal Moment? RitualCompletedAt { get; private set; }

    /// <summary>
    /// When this block's session ended, early or on time — a faithful copy of the session's
    /// own EndedAt, written in the same save; null while no session has ended (Gap 86). An
    /// early end is simply one before End: every reader compares it to End itself.
    /// </summary>
    internal Moment? SessionEndedAt { get; private set; }

    /// <summary>
    /// What this block covers of other blocks' deficits — <see cref="RedemptionReceipt.None"/>
    /// unless it is a Redemption block (Gap 106).
    /// </summary>
    public RedemptionReceipt Receipt { get; private set; } = RedemptionReceipt.None;

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
    /// for. <c>IBlockRepository.GetUnresolvedFocusBlocksAsync</c> asks the same rule in SQL
    /// through <see cref="TimeSlot.Filters.HasEnded"/> and <see cref="Filters.NoRitualAndNotCancelled"/>;
    /// a repository test checks the two agree.
    /// </summary>
    public bool IsUnresolved(Moment asOf)
        => HasEnded(asOf) && RitualCompletedAt is null && CancelledAt is null;

    /// <summary>
    /// The block's rules as expressions a repository hands to the database, since the stamps they
    /// read are Domain-only (Gap 95) — the same idea as <see cref="TimeSlot.Filters"/>.
    /// </summary>
    public static class Filters
    {
        /// <summary>The stamp half of <see cref="IsUnresolved"/>: Ritual never completed, not cancelled.</summary>
        public static Expression<Func<FocusBlock, bool>> NoRitualAndNotCancelled { get; } =
            b => b.RitualCompletedAt == null && b.CancelledAt == null;
    }

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
    /// cut at <paramref name="asOf"/>, rounded down to whole minutes (<see cref="WholeMinutes.RoundDown"/>) —
    /// the one rounding in focus accounting: a block is whole minutes long, so its deficit needs none
    /// (Gap 92, V10). Null when nothing is earned.
    /// </summary>
    public TimeSlot? CreditedSlot(Moment asOf)
    {
        if (WorkSlot() is not { } worked) return null;

        var end = asOf < worked.End ? asOf : worked.End;
        var credit = WholeMinutes.RoundDown(end - worked.Start);
        return credit > WholeMinutes.Zero ? worked.Until(worked.Start + credit.AsTimeSpan) : null;
    }

    /// <summary>The whole minutes <see cref="CreditedSlot"/> holds — zero when nothing is earned.</summary>
    internal WholeMinutes CreditAt(Moment asOf) =>
        CreditedSlot(asOf) is { } slot ? WholeMinutes.TryExact(slot.Length) ?? WholeMinutes.Zero : WholeMinutes.Zero; // exact by construction

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
    /// deficit never becomes a deficit source itself (Rule 13). Whole
    /// minutes: the block's whole-minute length less its whole-minute credit, nothing rounded.
    /// </summary>
    internal WholeMinutes OwnDeficit(Moment asOf)
    {
        if (IsRedemption || !IsFinal(asOf) || Outcome(asOf) == FocusBlockOutcome.CompletedOnTime)
            return WholeMinutes.Zero;

        return Length.Less(CreditAt(asOf));
    }

    /// <summary>Once the Ritual is done, a session owns this block's ending and announces it.</summary>
    protected override bool AnnouncesOwnEnd => RitualCompletedAt is null;

    public static FocusBlock Create(
        Guid id,
        Guid blockPresetId,
        TimeSlot scheduled,
        string? title = null,
        ContractMembership? membership = null)
        => CreateWithReceipt(id, blockPresetId, scheduled, title, membership, RedemptionReceipt.None);

    /// <summary>
    /// A block holding <paramref name="receipt"/>. Only the Domain writes a receipt (Gap 106,
    /// decision 8): <see cref="NenContract.PlaceRedemptionBlock"/> when a redemption block is
    /// placed, <see cref="SplitInto"/> when one is split.
    /// </summary>
    internal static FocusBlock CreateWithReceipt(
        Guid id,
        Guid blockPresetId,
        TimeSlot scheduled,
        string? title,
        ContractMembership? membership,
        RedemptionReceipt receipt)
    {
        ValidateDuration(scheduled);
        if (receipt is { Entries.Count: > 0 } && membership is not { IsRedemption: true })
            throw new ArgumentException("Only a redemption block can cover a deficit.", nameof(receipt));

        return new FocusBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Scheduled = scheduled,
            Title = title,
            Membership = membership,
            Receipt = receipt
        };
    }

    /// <summary>
    /// The two blocks that replace this one when it is split: each keeps its preset, title and
    /// membership, and the receipt is divided in time order (<see cref="RedemptionReceipt.SplitAt"/>).
    /// A split never recalculates and never shortens a piece (Gap 106, decision 2). Throws unless
    /// the pieces add up to this block's length (Gap 95, decision 16).
    /// </summary>
    public (FocusBlock First, FocusBlock Second) SplitInto(Guid firstId, TimeSlot first, Guid secondId, TimeSlot second)
    {
        if (first.Length + second.Length != Scheduled.Length)
            throw new ArgumentException("The two pieces must add up to the block's length.");

        var firstLength = WholeMinutes.TryExact(first.Length)
            ?? throw new ArgumentException("A planned block is a whole number of minutes long.", nameof(first));
        var (firstReceipt, secondReceipt) = Receipt.SplitAt(firstLength);
        return (CreateWithReceipt(firstId, BlockPresetId, first, Title, Membership, firstReceipt),
            CreateWithReceipt(secondId, BlockPresetId, second, Title, Membership, secondReceipt));
    }

    public override void Reschedule(TimeSlot scheduled)
    {
        ValidateDuration(scheduled);
        base.Reschedule(scheduled);
    }

    private static void ValidateDuration(TimeSlot slot)
    {
        if (!FitsMaxLength(slot))
            throw new ArgumentException($"A Focus block cannot exceed {MaxLength.TotalHours} hours.");
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
    /// session can end on a block whose Ritual wasn't done, or before it was (Gap 95). Stamped
    /// only by its session's <see cref="Session.Complete"/> (Gap 89 Slice A), so it is internal.
    /// </summary>
    internal void MarkSessionEnded(Moment endedAt)
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
    /// A redemption block can never claim more scheduled time than it actually redeems —
    /// covering less than what's owed is fine (the rest stays outstanding for a later
    /// block), but claiming more would let unaccounted "extra" time hide inside a block
    /// whose REDEMPTION badge implies its whole span is accounted-for deficit coverage.
    /// Pure and self-contained (only this block's own Start/End and receipt), so it
    /// lives here rather than in an Application handler — the same justification already
    /// used for MaxLength/ValidateDuration on this entity. Exact: the receipt is whole
    /// minutes, so the capped end is too and nothing is rounded (Gap 92, V10 — this used to need a
    /// long argument with MODULE_STANDARDS.md's "round a fraction before persisting" rule).
    /// </summary>
    internal static TimeSlot CapEndToRedemptionCoverage(TimeSlot requested, RedemptionReceipt receipt)
    {
        if (receipt.Entries.Count == 0) return requested;

        var covered = receipt.Total.AsTimeSpan;
        return covered >= requested.Length ? requested : requested.Until(requested.Start + covered);
    }
}
