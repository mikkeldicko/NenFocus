namespace NenFocus.Domain.ValueObjects;

public record WorkHours
{
    public TimeOnly Start { get; }
    public TimeOnly End { get; }

    public WorkHours(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        Start = start;
        End = end;
    }

    /// <summary>
    /// Standard interval containment: the block's time-of-day range must fit inside
    /// [Start, End] — a block ending exactly at End (e.g. a 9-to-5 block against 9:00-17:00
    /// work hours) is valid; only time strictly outside the window is rejected.
    /// </summary>
    public bool ContainsRange(DateTime start, DateTime end)
    {
        var startTime = TimeOnly.FromDateTime(start);
        var endTime = TimeOnly.FromDateTime(end);
        return startTime >= Start && endTime <= End;
    }
}
