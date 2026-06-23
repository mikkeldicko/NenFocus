namespace NenFocus.Domain.Entities;

public sealed class RitualBlock : PlannedBlock
{
    /// <summary>Null means the Ritual was never completed for this block.</summary>
    public DateTime? RitualCompletedAt { get; private set; }

    private RitualBlock() { }

    public static RitualBlock Create(
        Guid id,
        Guid blockProfileId,
        DateTime start,
        DateTime end,
        string? title = null)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        return new RitualBlock
        {
            Id = id,
            BlockProfileId = blockProfileId,
            Start = start,
            End = end,
            Title = title
        };
    }

    public void MarkRitualCompleted(DateTime completedAt)
    {
        if (RitualCompletedAt.HasValue)
            throw new InvalidOperationException(
                "Ritual has already been marked complete for this block.");

        RitualCompletedAt = completedAt;
    }
}