namespace NenFocus.Domain.Entities;

public sealed class BackgroundBlock : PlannedBlock
{
    private BackgroundBlock() { }

    public static BackgroundBlock Create(
        Guid id,
        Guid blockProfileId,
        DateTime start,
        DateTime end,
        string? title = null)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        return new BackgroundBlock
        {
            Id = id,
            BlockProfileId = blockProfileId,
            Start = start,
            End = end,
            Title = title
        };
    }
}