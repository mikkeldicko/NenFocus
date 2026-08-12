using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public sealed class FocusBlock : PlannedBlock
{
    /// <summary>Null means the Ritual was never completed for this block.</summary>
    public DateTime? RitualCompletedAt { get; private set; }

    /// <summary>Null unless the session ended before this block's scheduled End.</summary>
    public DateTime? SessionEndedAt { get; private set; }

    private FocusBlock() { }

    public static FocusBlock Create(
        Guid id,
        Guid blockPresetId,
        DateTime start,
        DateTime end,
        string? title = null,
        Guid? contractId = null,
        IReadOnlyList<MakeUpForEntry>? makeUpForEntries = null)
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
            ContractId = contractId,
            MakeUpForEntries = makeUpForEntries ?? Array.Empty<MakeUpForEntry>()
        };
    }

    public void MarkRitualCompleted(DateTime completedAt)
    {
        if (RitualCompletedAt.HasValue)
            throw new InvalidOperationException(
                "Ritual has already been marked complete for this block.");

        RitualCompletedAt = completedAt;
    }

    public void MarkSessionEnded(DateTime endedAt)
    {
        if (SessionEndedAt.HasValue)
            throw new InvalidOperationException(
                "Session end has already been marked for this block.");

        SessionEndedAt = endedAt;
    }

    /// <summary>
    /// Replaces which missed/late blocks this make-up block covers. A plain setter, same
    /// as AssignPreset/AssignContract — same-preset, uncovered-deficit, and same-day
    /// constraints are Application-layer concerns, not enforced here.
    /// </summary>
    public void AssignMakeUpForEntries(IReadOnlyList<MakeUpForEntry> entries)
    {
        MakeUpForEntries = entries;
    }
}
