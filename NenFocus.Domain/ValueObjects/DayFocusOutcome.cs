namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A single day's Focus outcome, supplied by the Application layer, used to
/// evaluate whether a Nen Contract day was met.
/// </summary>
public record DayFocusOutcome
{
    public double CompletedFocusHours { get; }
    public bool HasMissedFocusBlock { get; }

    public DayFocusOutcome(double completedFocusHours, bool hasMissedFocusBlock)
    {
        if (completedFocusHours < 0)
            throw new ArgumentException("Completed focus hours cannot be negative.");

        CompletedFocusHours = completedFocusHours;
        HasMissedFocusBlock = hasMissedFocusBlock;
    }
}
