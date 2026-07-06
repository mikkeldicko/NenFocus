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

    protected PlannedBlock() { }

    public bool OverlapsWith(PlannedBlock other)
        => Start < other.End && End > other.Start;

    public bool IsActive(DateTime atTime)
        => atTime >= Start && atTime < End;

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