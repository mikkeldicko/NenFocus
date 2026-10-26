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

    /// <summary>How this block was booked — Planned in advance, or Spontaneous (a walk-in) at arrival (Gap 114, D2).</summary>
    public FocusBlockBookingType BookingType { get; private set; }

    /// <summary>
    /// This block's frozen result, written once by its own final transition — its session ended,
    /// it was missed, or it was cancelled (Gap 114, D5). Null while the block's story is still
    /// open; every answer below reads it once it exists, and derives live otherwise.
    /// </summary>
    public FocusBlockResult? Result { get; private set; }

    /// <summary>A missed block's own note on what got in the way (Gap 114, D3); a run's reflection lives on its Session.</summary>
    public Reflection Reflection { get; private set; } = new(null);

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
    /// Window closed, Ritual never completed, not cancelled, and not yet frozen — what the
    /// missed-block sweep looks for. <c>IBlockRepository.GetUnresolvedFocusBlocksAsync</c> asks
    /// the same rule in SQL through <see cref="TimeSlot.Filters.HasEnded"/> and
    /// <see cref="Filters.NoRitualAndNotCancelled"/>; a repository test checks the two agree.
    /// </summary>
    public bool IsUnresolved(Moment asOf)
        => Result is null && HasEnded(asOf) && RitualCompletedAt is null && CancelledAt is null;

    /// <summary>
    /// The block's rules as expressions a repository hands to the database, since the stamps they
    /// read are Domain-only (Gap 95) — the same idea as <see cref="TimeSlot.Filters"/>.
    /// </summary>
    public static class Filters
    {
        /// <summary>The stamp half of <see cref="IsUnresolved"/>: not frozen, Ritual never completed, not cancelled.</summary>
        public static Expression<Func<FocusBlock, bool>> NoRitualAndNotCancelled { get; } =
            b => b.Result == null && b.RitualCompletedAt == null && b.CancelledAt == null;
    }

    /// <summary>
    /// Past the grace period at <paramref name="moment"/> — the one lateness rule, asked with the
    /// Ritual stamp (<see cref="StartedLate"/>) and with "now" (the Ritual's late notice).
    /// </summary>
    public bool IsLateAt(Moment moment) => moment > Scheduled.Start + TimeSpan.FromMinutes(GracePeriodMinutes);

    /// <summary>The Ritual was completed past the grace period, but before the block's end — read off
    /// the worked span, so it stays true once the result is frozen (Gap 114, D5).</summary>
    public bool StartedLate => WorkSlot() is { } work && work.Start > Scheduled.Start;

    /// <summary>The session ended before the scheduled end — read off the worked span.</summary>
    public bool EndedEarly => WorkSlot() is { } work && work.End < Scheduled.End;

    /// <summary>
    /// How the block resolved, or null while it could still be started (before its end, Ritual
    /// not done). One verdict per block, in this priority: Missed, EndedEarly, StartedLate,
    /// CompletedOnTime — <see cref="StartedLate"/> and <see cref="EndedEarly"/> can both be true.
    /// Frozen once the block is final; derived live until then.
    /// </summary>
    public FocusBlockOutcome? Outcome(Moment asOf)
    {
        if (Result is { } result) return result.Outcome;
        if (RitualCompletedAt is not { } ritualCompletedAt)
            return HasEnded(asOf) ? FocusBlockOutcome.Missed : null;
        if (Scheduled.HasEnded(ritualCompletedAt)) return FocusBlockOutcome.Missed;
        if (EndedEarly) return FocusBlockOutcome.EndedEarly;
        return StartedLate ? FocusBlockOutcome.StartedLate : FocusBlockOutcome.CompletedOnTime;
    }

    /// <summary>
    /// The whole span the block is worked over: from the scheduled start when the Ritual came within
    /// grace (blocking was already running), else from the Ritual; to the session's end when it ended
    /// early, else the scheduled end. Once the block is final this is <see cref="Result"/>'s frozen
    /// WorkSlot — the one grace-dependent fact, stored so a later rule change can't re-grade the past
    /// (Gap 114, D5). Null when nothing was worked. The grace and early-end rules live only here.
    /// </summary>
    public TimeSlot? WorkSlot()
    {
        if (Result is not null) return Result.WorkSlot;
        if (RitualCompletedAt is not { } ritualCompletedAt) return null;

        var start = IsLateAt(ritualCompletedAt) ? ritualCompletedAt : Scheduled.Start;
        return TimeSlot.TryCreate(start, Scheduled.End);
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
    /// The block's story has concluded: its result is frozen, or it was missed by the clock (its
    /// window closed with no Ritual). A session still running isn't final — it could still end
    /// early (Redemption Rule 18).
    /// </summary>
    public override bool IsFinal(Moment asOf)
        => Result is not null || Outcome(asOf) == FocusBlockOutcome.Missed;

    /// <summary>
    /// What this block failed to deliver of its own schedule — its side of the deficit ledger,
    /// however many blocks have since tried to redeem it. Zero until final, zero when it was
    /// completed on time, zero when it was cancelled (a broken contract's block owes nothing),
    /// and zero for a redemption block: a block covering someone else's deficit never becomes a
    /// deficit source itself (Rule 13). Whole minutes: the block's whole-minute length less its
    /// whole-minute credit, nothing rounded.
    /// </summary>
    internal WholeMinutes OwnDeficit(Moment asOf)
    {
        if (IsRedemption || !IsFinal(asOf)
            || Outcome(asOf) is FocusBlockOutcome.CompletedOnTime or FocusBlockOutcome.Cancelled)
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
    /// A walk-in block (Gap 114, D2): booked Spontaneous — created at the moment the user starts,
    /// never booked ahead. Its Ritual is done at creation in the sense that the block is never saved
    /// unarrived; the arrival itself is still stamped by <see cref="Session.Start"/>, the one writer.
    /// </summary>
    public static FocusBlock Spontaneous(
        Guid id,
        Guid blockPresetId,
        TimeSlot scheduled,
        string? title = null,
        ContractMembership? membership = null)
        => CreateWithReceipt(id, blockPresetId, scheduled, title, membership, RedemptionReceipt.None, FocusBlockBookingType.Spontaneous);

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
        RedemptionReceipt receipt,
        FocusBlockBookingType bookingType = FocusBlockBookingType.Planned)
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
            BookingType = bookingType,
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
        return (CreateWithReceipt(firstId, BlockPresetId, first, Title, Membership, firstReceipt, BookingType),
            CreateWithReceipt(secondId, BlockPresetId, second, Title, Membership, secondReceipt, BookingType));
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

    /// <summary>The arrival — written only by <see cref="Session.Start"/>, the one writer for both
    /// booking types (Gap 114, D6). Internal so nothing else can stamp it.</summary>
    internal void MarkRitualCompleted(Moment completedAt)
    {
        if (RitualCompletedAt.HasValue)
            throw new InvalidOperationException(
                "Ritual has already been marked complete for this block.");

        RitualCompletedAt = completedAt;
    }

    /// <summary>
    /// Freezes the block's result from its session's end (Gap 114, D5, D6): the worked span and
    /// the verdict, computed once and never recomputed. Refuses a stamp the block's own answers
    /// (late, outcome, credit) can't interpret: no session can end on a block whose Ritual wasn't
    /// done, or before it was (Gap 95). Called only by its session's <see cref="Session.Complete"/>
    /// (Gap 89 Slice A), so it is internal.
    /// </summary>
    internal void MarkSessionEnded(Moment endedAt)
    {
        if (Result is not null)
            throw new InvalidOperationException(
                "The block's result is already frozen.");
        if (RitualCompletedAt is not { } ritualCompletedAt)
            throw new InvalidOperationException(
                "A session can't end on a block whose Ritual was never completed.");
        if (Scheduled.HasEnded(ritualCompletedAt))
            throw new InvalidOperationException(
                "A session can't end on a block whose Ritual completed after it ended.");
        if (endedAt <= ritualCompletedAt)
            throw new ArgumentException(
                "A session must run some time before it can end.", nameof(endedAt));

        var start = IsLateAt(ritualCompletedAt) ? ritualCompletedAt : Scheduled.Start;
        // The worked span never runs past the block's scheduled end — the same cap Session.Complete
        // already applies, owned here too so a frozen result can never exceed its own block.
        var end = endedAt < Scheduled.End ? endedAt : Scheduled.End;
        var workSlot = TimeSlot.TryCreate(start, end);
        Result = new FocusBlockResult(OutcomeFor(workSlot), workSlot);
    }

    /// <summary>
    /// Freezes a block the sweep found unresolved (window closed, Ritual never done, not cancelled,
    /// not yet frozen) as Missed (Gap 114, D5). The sweep's own rule, asked here so no caller can
    /// skip it. A missed block ran nothing, so it has no work and no credit.
    /// </summary>
    public void MarkMissed(Moment asOf)
    {
        if (!IsUnresolved(asOf))
            throw new InvalidOperationException("Only an unresolved block can be marked missed.");
        Result = new FocusBlockResult(FocusBlockOutcome.Missed, null);
    }

    /// <summary>
    /// Cancelling a Focus block also freezes its result as Cancelled (Gap 114, D5 amd 2) — a
    /// cancelled block is not Missed, and owes no deficit.
    /// </summary>
    public override void MarkCancelled(Moment cancelledAt)
    {
        if (Result is not null)
            throw new InvalidOperationException("A block whose result is frozen can't be cancelled.");
        base.MarkCancelled(cancelledAt);
        Result = new FocusBlockResult(FocusBlockOutcome.Cancelled, null);
    }

    /// <summary>The one verdict per block, in this priority: Missed, EndedEarly, StartedLate,
    /// CompletedOnTime — StartedLate and EndedEarly can both be true (Gap 95).</summary>
    private FocusBlockOutcome OutcomeFor(TimeSlot? workSlot)
    {
        // MarkSessionEnded is only reached once the Ritual is done, so a null work slot means the
        // session ended at its very start — zero work, i.e. ended early. Missed is MarkMissed's.
        if (workSlot is null) return FocusBlockOutcome.EndedEarly;
        if (workSlot.End < Scheduled.End) return FocusBlockOutcome.EndedEarly;
        if (workSlot.Start > Scheduled.Start) return FocusBlockOutcome.StartedLate;
        return FocusBlockOutcome.CompletedOnTime;
    }

    /// <summary>The user's note on why this block was missed — a missed block holds its own (Gap 114,
    /// D3); a run's reflection lives on its Session.</summary>
    public void SetReflection(string? text)
    {
        if (Result?.Outcome != FocusBlockOutcome.Missed)
            throw new InvalidOperationException("Only a missed block has a reflection.");
        Reflection = this.Reflection.WithText(text);
    }

    /// <summary>Dismissing the miss's recap screen — mirrors Session's acknowledgement (Gap 114, D3).</summary>
    public void MarkPostSessionAcknowledged(Moment acknowledgedAt)
    {
        if (Result?.Outcome != FocusBlockOutcome.Missed)
            throw new InvalidOperationException("Only a missed block has a post-session acknowledgement.");
        Reflection = this.Reflection.Acknowledged(acknowledgedAt);
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
        return Result?.WorkSlot is { } work && work.End < end ? work.End : end;
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
