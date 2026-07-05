namespace NenFocus.Domain.ValueObjects;

public record ChangeWindow
{
    public TimeOnly Start { get; }
    public TimeOnly End { get; }

    /// <summary>
    /// The configured daily use limit only. Tracking how many uses remain on a
    /// given day is stateful and belongs to the Application layer, not here.
    /// </summary>
    public int UsesPerDay { get; }

    public ChangeWindow(TimeOnly start, TimeOnly end, int usesPerDay)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        if (usesPerDay < 1)
            throw new ArgumentException("Uses per day must be at least 1.");

        Start = start;
        End = end;
        UsesPerDay = usesPerDay;
    }

    public bool Contains(TimeOnly candidateTime) => candidateTime >= Start && candidateTime < End;
}
