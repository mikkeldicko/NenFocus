namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed record FocusBlockSchedule
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public int TotalMinutes => (int)(End - Start).TotalMinutes;
}
