using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public abstract class PlannedBlock : AggregateRoot
{
    public Guid Id { get; protected set; }
    public Guid BlockPresetId { get; protected set; }
    /// <summary>
    /// The time this block was planned for — its one source of truth for when it is (Gap 88).
    /// What it really occupies (early end, missed, cancelled) is <see cref="ReservedSlot"/>.
    /// </summary>
    public TimeSlot Scheduled { get; protected set; } = null!;

    public string? Title { get; protected set; }

    private ContractMembership? _membership;

    /// <summary>
    /// Null means a plain block; set means it belongs to that Nen Contract, in that role (Gap 97).
    /// Each block keeps its own copy: the membership is stored as part of the block's row, and
    /// one instance can't be part of two rows (a split's halves, a drafted batch).
    /// </summary>
    public ContractMembership? Membership
    {
        get => _membership;
        protected set => _membership = value is null ? null : value with { };
    }

    /// <summary>Null means still scheduled/live; set means the owning Nen Contract was broken and this
    /// block was cut short before it ran. Applies uniformly to FocusBlock and BackgroundBlock.</summary>
    public Moment? CancelledAt { get; protected set; }

    /// <summary>
    /// Empty means an ordinary block. Populated means this is a redemption block covering
    /// those specific missed/late blocks' deficits. Only ever populated on a FocusBlock —
    /// Background Blocks have no sessions and cannot contribute toward a Focus-hours deficit.
    /// </summary>
    public IReadOnlyList<RedemptionEntry> RedemptionEntries { get; protected set; } = Array.Empty<RedemptionEntry>();

    protected PlannedBlock() { }


    public bool OverlapsWith(PlannedBlock other)
        => Scheduled.Overlaps(other.Scheduled);

    /// <summary>
    /// True when <paramref name="atTime"/> falls within this block's still-actually-reserved
    /// window — bounded by <see cref="EffectiveEnd"/>, not the raw <see cref="End"/>, so a
    /// block whose session ended early stops being "active" the moment it actually ended,
    /// not at its original scheduled end. Without this, a block that freed its tail for new
    /// scheduling (the exact reason EffectiveEnd exists) could still be picked by
    /// PolicyEngine as "the" active Focus block over a genuinely new one scheduled into that
    /// freed tail, permanently suppressing the new block's Ritual trigger until the stale
    /// block's raw window finally closed.
    /// </summary>
    public bool IsActive(Moment at)
        => ReservedSlot(at)?.Contains(at) ?? false;

    /// <summary>
    /// The time this block really occupies as of <paramref name="asOf"/>: its scheduled slot cut
    /// short by <see cref="EffectiveEnd"/> (early end, missed, cancelled); null when it occupies
    /// nothing any more.
    /// </summary>
    public TimeSlot? ReservedSlot(Moment asOf)
    {
        var end = EffectiveEnd(asOf);
        return end > Scheduled.Start ? new TimeSlot(Scheduled.Start, end) : null;
    }

    /// <summary>
    /// True once this block's window has fully closed — at exactly its end, since Gap 88's
    /// half-open rule. Canonical definition of "past" — e.g. a broken Nen Contract's cleanup must
    /// only ever touch blocks this returns false for; blocks it returns true for stay linked for
    /// historical accuracy.
    /// </summary>
    public bool HasEnded(Moment asOf) => Scheduled.HasEnded(asOf);

    /// <summary>
    /// True from its start onward. Once started, a block is locked: no edit, no split, and no
    /// delete until it has ended (Gap 88 phase 0).
    /// </summary>
    public bool HasStarted(Moment asOf) => Scheduled.HasStarted(asOf);

    /// <summary>
    /// Where this block's reserved time ends, as of <paramref name="asOf"/> — the boundary
    /// <see cref="ReservedSlot"/> is built on, so a block that no longer occupies its full
    /// scheduled span (e.g. a Focus session that ended early) frees the rest for new scheduling
    /// without ever changing <see cref="Scheduled"/> (which stays the historical record used by
    /// Daily Floor / deficit scoring). Every reason the window shrinks lives here, so
    /// <see cref="IsActive"/>, overlap checks and markers never check those reasons themselves.
    /// Base: cancellation — the window ends at the cancel moment, or is empty if cancelled before
    /// it started. <see cref="FocusBlock"/> adds its own reasons.
    /// </summary>
    public virtual Moment EffectiveEnd(Moment asOf)
    {
        if (CancelledAt is not { } cancelledAt) return Scheduled.End;
        if (cancelledAt <= Scheduled.Start) return Scheduled.Start;
        return cancelledAt < Scheduled.End ? cancelledAt : Scheduled.End;
    }

    public TimeSpan GetDuration() => Scheduled.Length;

    /// <summary>How long before its start the user is warned that a block is coming.</summary>
    public static readonly TimeSpan UpcomingWarningLead = TimeSpan.FromMinutes(5);

    /// <summary>
    /// What this block is called to the user: its own title if it has one, otherwise its
    /// preset's name — the one definition of a block's name. Notifications use it. The Planner
    /// tile still shows the preset name directly; identical today since nothing sets a title
    /// yet — route the tile through this when titles become editable.
    /// </summary>
    public string DisplayName(string presetName) => Title is { Length: > 0 } title ? title : presetName;

    /// <summary>Focus or Background — read off the subtype; there is no stored column.</summary>
    public abstract BlockPresetType PresetType { get; }

    /// <summary>
    /// Whether this block owns the story of its own ending. A Focus block whose session ran
    /// doesn't: its end is its session's end, which the session announces itself.
    /// </summary>
    protected virtual bool AnnouncesOwnEnd => true;

    /// <summary>
    /// The markers on this block's timeline the clock passed moving from <paramref name="from"/> to
    /// <paramref name="to"/>, half-open (from, to] — a caller that passes each tick's end as
    /// the next tick's start hears about every marker exactly once. StartingSoon: the
    /// warning moment (start − <see cref="UpcomingWarningLead"/>) was crossed
    /// and the block hasn't started. Started/Ended: <see cref="IsActive"/> flipped — the same
    /// definition PolicyEngine enforces, so the user is told exactly when blocking changes.
    /// Ended only if <see cref="AnnouncesOwnEnd"/>. A clock that moved backwards, or jumped
    /// over the whole block, passed nothing worth telling. A block cancelled before it
    /// started reserves no time, so passes nothing; one cancelled mid-run ends then.
    /// </summary>
    public IReadOnlyList<BlockMarker> MarkersPassedBetween(Moment from, Moment to)
    {
        if (to <= from) return [];

        var markers = new List<BlockMarker>();

        var warning = Scheduled.Start - UpcomingWarningLead;
        if (ReservedSlot(to) is not null && from < warning && warning <= to && !Scheduled.HasStarted(to))
            markers.Add(new BlockMarker(BlockMarkerKind.StartingSoon, warning.Utc));

        var wasActive = IsActive(from);
        var isActive = IsActive(to);
        if (!wasActive && isActive)
            markers.Add(new BlockMarker(BlockMarkerKind.Started, Scheduled.Start.Utc));
        else if (wasActive && !isActive && AnnouncesOwnEnd)
            markers.Add(new BlockMarker(BlockMarkerKind.Ended, EffectiveEnd(from).Utc));

        return markers;
    }

    public virtual void Reschedule(TimeSlot scheduled)
    {
        Scheduled = scheduled;
    }

    public void AssignPreset(Guid presetId)
    {
        BlockPresetId = presetId;
    }

    /// <summary>A plain block becomes a member. A block is in one contract at most, and its role never changes.</summary>
    public void Join(ContractMembership membership)
    {
        if (Membership is not null)
            throw new InvalidOperationException("This block already belongs to a contract.");

        Membership = membership;
    }

    /// <summary>
    /// A Bonus block becomes a plain block again. Committed and Redemption blocks never leave
    /// their contract.
    /// </summary>
    public void Leave()
    {
        if (Membership is not { IsBonus: true })
            throw new InvalidOperationException("Only a Bonus block can leave its contract.");

        Membership = null;
    }

    /// <summary>
    /// After being placed or moved: a plain or Bonus block's membership follows where it sits —
    /// under <paramref name="governing"/> it is that contract's Bonus block, under none it is a
    /// plain block. A Committed or Redemption block is unaffected: it never leaves its contract.
    /// </summary>
    public void PlaceUnder(NenContract? governing)
    {
        if (Membership is { IsBonus: false }) return;
        if (Membership?.ContractId == governing?.Id) return;

        if (Membership is not null) Leave();
        if (governing is not null) Join(governing.MembershipForPlacedBlock(claimsDeficit: false));
    }

    public void MarkCancelled(Moment cancelledAt)
    {
        if (CancelledAt.HasValue)
            throw new InvalidOperationException(
                "This block has already been marked cancelled.");

        CancelledAt = cancelledAt;
        Raise(new PlannedBlockCancelledEvent(Id, cancelledAt));
    }
}
