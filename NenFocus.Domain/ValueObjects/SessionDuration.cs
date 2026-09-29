namespace NenFocus.Domain.ValueObjects;

public record SessionDuration
{
    public TimeSpan Value { get; }

    public SessionDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
            throw new ArgumentException("Duration must be at least 1 minute.");

        if (value.TotalHours > 24)
            throw new ArgumentException("Duration cannot exceed 24 hours.");

        Value = value;
    }

    public static SessionDuration FromHoursAndMinutes(int hours, int minutes)
        => new SessionDuration(new TimeSpan(hours, minutes, 0));
}