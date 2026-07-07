namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// Ties a make-up Focus Block to one specific missed or late Focus Block it covers.
/// The Application layer enforces that MinutesCovered does not exceed the referenced
/// block's remaining deficit and that all linked blocks share the make-up block's preset.
/// </summary>
public record MakeUpForEntry
{
    public Guid MissedBlockId { get; }
    public double MinutesCovered { get; }

    public MakeUpForEntry(Guid missedBlockId, double minutesCovered)
    {
        if (minutesCovered <= 0)
            throw new ArgumentException("Minutes covered must be greater than zero.", nameof(minutesCovered));

        MissedBlockId = missedBlockId;
        MinutesCovered = minutesCovered;
    }
}
