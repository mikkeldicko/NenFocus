using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public abstract class PlannedBlock : AggregateRoot
{
    public Guid Id { get; protected set; }
    public Guid BlockPresetId { get; protected set; }
    public DateTime Start { get; protected set; }
    public DateTime End { get; protected set; }
    public string? Title { get; protected set; }

    /// <summary>Null means an ordinary, uncommitted block; set means the block is under that Nen Contract.</summary>
    public Guid? ContractId { get; protected set; }

    /// <summary>Null means still scheduled/live; set means the owning Nen Contract was broken and this
    /// block was cut short before it ran. Applies uniformly to FocusBlock and BackgroundBlock.</summary>
    public DateTime? CancelledAt { get; protected set; }

    /// <summary>
    /// Empty means an ordinary block. Populated means this is a redemption block covering
    /// those specific missed/late blocks' deficits. Only ever populated on a FocusBlock —
    /// Background Blocks have no sessions and cannot contribute toward a Focus-hours deficit.
    /// </summary>
    public IReadOnlyList<RedemptionEntry> RedemptionEntries { get; protected set; } = Array.Empty<RedemptionEntry>();

    protected PlannedBlock() { }

    public bool OverlapsWith(PlannedBlock other)
        => Start < other.End && End > other.Start;

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
    public bool IsActive(DateTime atTime)
        => atTime >= Start && atTime < EffectiveEnd(atTime);

    /// <summary>
    /// True once this block's window has fully closed. Canonical definition of "past" —
    /// e.g. a broken Nen Contract's cleanup must only ever touch blocks this returns
    /// false for; blocks it returns true for stay linked for historical accuracy.
    /// </summary>
    public bool HasEnded(DateTime asOf) => End < asOf;

    /// <summary>
    /// True from <see cref="Start"/> onward. Once started, a block is locked: no edit, no
    /// split, and no delete until it has ended (Gap 88 phase 0).
    /// </summary>
    public bool HasStarted(DateTime asOf) => Start <= asOf;

    /// <summary>
    /// How much of this block's window is still actually reserved, as of <paramref name="asOf"/> —
    /// the boundary overlap-checks use instead of <see cref="End"/>, so a block that no longer
    /// occupies its full scheduled span (e.g. a Focus session that ended early) frees the rest
    /// for new scheduling without ever mutating <see cref="End"/> itself (which stays the
    /// historical record used by Daily Floor / deficit scoring). Every reason the window
    /// shrinks lives here, so <see cref="IsActive"/>, overlap checks and markers never check
    /// those reasons themselves. Base: cancellation — the window ends at the cancel moment, or
    /// is empty if cancelled before it started. <see cref="FocusBlock"/> adds its own reasons.
    /// </summary>
    public virtual DateTime EffectiveEnd(DateTime asOf)
    {
        if (CancelledAt is not { } cancelledAt) return End;
        if (cancelledAt <= Start) return Start;
        return cancelledAt < End ? cancelledAt : End;
    }

    public TimeSpan GetDuration() => End - Start;

    public int MinutesUntilStart(DateTime asOf) => (int)(Start - asOf).TotalMinutes;

    /// <summary>How long before <see cref="Start"/> the user is warned that a block is coming.</summary>
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
    /// warning moment (<see cref="Start"/> − <see cref="UpcomingWarningLead"/>) was crossed
    /// and the block hasn't started. Started/Ended: <see cref="IsActive"/> flipped — the same
    /// definition PolicyEngine enforces, so the user is told exactly when blocking changes.
    /// Ended only if <see cref="AnnouncesOwnEnd"/>. A clock that moved backwards, or jumped
    /// over the whole block, passed nothing worth telling. A block cancelled before it
    /// started reserves no time, so passes nothing; one cancelled mid-run ends then.
    /// </summary>
    public IReadOnlyList<BlockMarker> MarkersPassedBetween(DateTime from, DateTime to)
    {
        if (to <= from) return [];

        var markers = new List<BlockMarker>();

        var warningMoment = Start - UpcomingWarningLead;
        var stillReservesTime = EffectiveEnd(to) > Start;
        if (stillReservesTime && warningMoment > from && warningMoment <= to && to < Start)
            markers.Add(new BlockMarker(BlockMarkerKind.StartingSoon, warningMoment));

        var wasActive = IsActive(from);
        var isActive = IsActive(to);
        if (!wasActive && isActive)
            markers.Add(new BlockMarker(BlockMarkerKind.Started, Start));
        else if (wasActive && !isActive && AnnouncesOwnEnd)
            markers.Add(new BlockMarker(BlockMarkerKind.Ended, EffectiveEnd(from)));

        return markers;
    }

    public virtual void Reschedule(DateTime start, DateTime end)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");
        Start = start;
        End = end;
    }

    public void TrimEnd(DateTime newEnd)
    {
        if (newEnd <= Start)
            throw new ArgumentException("New end time must be after start time.");
        End = newEnd;
    }

    public void AssignPreset(Guid presetId)
    {
        BlockPresetId = presetId;
    }

    public void AssignContract(Guid? contractId)
    {
        ContractId = contractId;
    }

    public void MarkCancelled(DateTime cancelledAt)
    {
        if (CancelledAt.HasValue)
            throw new InvalidOperationException(
                "This block has already been marked cancelled.");

        CancelledAt = cancelledAt;
        Raise(new PlannedBlockCancelledEvent(Id, cancelledAt));
    }
}