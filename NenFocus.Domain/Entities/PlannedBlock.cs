using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Entities;

public abstract class PlannedBlock : BaseEntity
{
    public Guid BlockProfileId { get; protected set; }

    // Backing properties kept as Start/End for compatibility with existing DB columns.
    // Expose StartTime/EndTime to satisfy the new API requirement.
    public DateTime Start { get; protected set; }
    public DateTime End { get; protected set; }

    public DateTime StartTime => Start;
    public DateTime EndTime => End;

    public string? Title { get; protected set; }

    public BlockType Type { get; protected set; }

    public bool IsPlanned { get; protected set; }

    protected PlannedBlock() { }

    public bool OverlapsWith(PlannedBlock other)
        => Start < other.End && End > other.Start;

    public bool IsActive(DateTime atTime)
        => atTime >= Start && atTime < End;

    public TimeSpan GetDuration() => End - Start;
}

