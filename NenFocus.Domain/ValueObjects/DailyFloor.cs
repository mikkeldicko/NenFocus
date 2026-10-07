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

    /// <summary>
    /// Pure numeric comparison used both at contract Seal (against planned Focus
    /// hours) and during later day-met evaluation (against completed Focus hours).
    /// </summary>
    public bool IsSatisfiedBy(double focusHours) => focusHours >= Hours;

    /// <summary>
    /// Whether a day whose window is <paramref name="window"/> long could meet this floor at all — the
    /// one floor-vs-window rule, asked by ContractDay, ContractCalendar and ContractPreset (Gap 92, V9).
    /// </summary>
    public bool FitsIn(TimeSpan window) => Hours <= window.TotalHours;
}
