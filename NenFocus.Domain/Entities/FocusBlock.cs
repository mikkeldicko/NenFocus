namespace NenFocus.Domain.Entities;

public sealed class FocusBlock : PlannedBlock
{
    /// <summary>Null means the Ritual was never completed for this block.</summary>
    public DateTime? RitualCompletedAt { get; private set; }

    private FocusBlock() { }

    public static FocusBlock Create(
        Guid id,
        Guid blockPresetId,
        DateTime start,
        DateTime end,
        string? title = null,
        Guid? contractId = null)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        return new FocusBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Start = start,
            End = end,
            Title = title,
            ContractId = contractId
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
