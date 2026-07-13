namespace NenFocus.Domain.ValueObjects;

public record DailyFloor
{
    public double Hours { get; }

    public DailyFloor(double hours)
    {
        if (hours < 0)
            throw new ArgumentException("Daily floor cannot be negative.");

        if (hours > 24)
            throw new ArgumentException("Daily floor cannot exceed 24 hours.");

        Hours = hours;
    }

    /// <summary>A floor of 0 hours — no minimum is enforced.</summary>
    public static DailyFloor None => new(0);

    /// <summary>
    /// Pure numeric comparison used both at contract Seal (against planned Focus
    /// hours) and during later day-met evaluation (against completed Focus hours).
    /// </summary>
    public bool IsSatisfiedBy(double focusHours) => focusHours >= Hours;

    /// <summary>
    /// Hours still needed to satisfy the floor, clamped at zero — exceeding the floor
    /// never reads as a negative "remaining" amount.
    /// </summary>
    public double HoursRemaining(double focusHours) => Math.Max(0, Hours - focusHours);
}
