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
    /// How much of this block's window is still actually reserved, as of <paramref name="asOf"/> —
    /// the boundary overlap-checks use instead of <see cref="End"/>, so a block that no longer
    /// occupies its full scheduled span (e.g. a Focus session that ended early) frees the rest
    /// for new scheduling without ever mutating <see cref="End"/> itself (which stays the
    /// historical record used by Daily Floor / deficit scoring). Base case: the whole window
    /// is still reserved, unconditionally — overridden by <see cref="FocusBlock"/>.
    /// </summary>
    public virtual DateTime EffectiveEnd(DateTime asOf) => End;

    public TimeSpan GetDuration() => End - Start;

    public int MinutesUntilStart(DateTime asOf) => (int)(Start - asOf).TotalMinutes;

    public void Reschedule(DateTime start, DateTime end)
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