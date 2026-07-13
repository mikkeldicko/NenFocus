using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public abstract class PlannedBlock
{
    public Guid Id { get; protected set; }
    public Guid BlockPresetId { get; protected set; }
    public DateTime Start { get; protected set; }
    public DateTime End { get; protected set; }
    public string? Title { get; protected set; }

    /// <summary>Null means an ordinary, uncommitted block; set means the block is under that Nen Contract.</summary>
    public Guid? ContractId { get; protected set; }

    /// <summary>
    /// Empty means an ordinary block. Populated means this is a make-up block covering
    /// those specific missed/late blocks' deficits. Only ever populated on a FocusBlock —
    /// Background Blocks have no sessions and cannot contribute toward a Focus-hours deficit.
    /// </summary>
    public IReadOnlyList<MakeUpForEntry> MakeUpForEntries { get; protected set; } = Array.Empty<MakeUpForEntry>();

    protected PlannedBlock() { }

    public bool OverlapsWith(PlannedBlock other)
        => Start < other.End && End > other.Start;

    public bool IsActive(DateTime atTime)
        => atTime >= Start && atTime < End;

    /// <summary>
    /// True once this block's window has fully closed. Canonical definition of "past" —
    /// e.g. a broken Nen Contract's cleanup must only ever touch blocks this returns
    /// false for; blocks it returns true for stay linked for historical accuracy.
    /// </summary>
    public bool HasEnded(DateTime asOf) => End < asOf;

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
}