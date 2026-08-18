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
    /// The window is anchored to start's calendar date and compared as DateTime, not
    /// TimeOnly — TimeOnly.FromDateTime would discard the date, so a block ending exactly
    /// at midnight (e.g. 19:00-24:00, represented as the next day's 00:00) would wrap to
    /// 00:00 and read as trivially "before" any End time instead of correctly failing.
    /// </summary>
    public bool ContainsRange(DateTime start, DateTime end)
    {
        var windowStart = start.Date + Start.ToTimeSpan();
        var windowEnd = start.Date + End.ToTimeSpan();
        return start >= windowStart && end <= windowEnd;
    }
}
