namespace NenFocus.Domain.ValueObjects;

public record BreakSettings
{
    public int IntervalMinutes { get; }
    public int BreakLengthMinutes { get; }
    public BreakSettings(int intervalMinutes, int breakLengthMinutes)
    {
        if (intervalMinutes is not (25 or 45 or 60))
            throw new ArgumentException("Interval must be 25, 45, or 60 minutes.");

        if (breakLengthMinutes is not (5 or 10 or 15))
            throw new ArgumentException("Break length must be 5, 10, or 15 minutes.");

        IntervalMinutes = intervalMinutes;
        BreakLengthMinutes = breakLengthMinutes;
    }
}