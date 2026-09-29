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
    /// completedHours ÷ Hours, uncapped (133% displays as 133%, never clamped to 100%) —
    /// null only when Hours is 0 (DailyFloor.None), since there's nothing meaningful to
    /// express a percentage of. See ARCHITECT_GOVERNANCE.md Open Gap C, C1.
    /// </summary>
    public double? PercentComplete(double completedHours) => Hours == 0 ? null : completedHours / Hours;
}
