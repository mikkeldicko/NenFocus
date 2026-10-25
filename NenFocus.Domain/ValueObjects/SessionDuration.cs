using NenFocus.Domain.Entities;

namespace NenFocus.Domain.ValueObjects;

public record SessionDuration
{
    public TimeSpan Value { get; }

    public SessionDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
            throw new ArgumentException("Duration must be at least 1 minute.");

        // One rule for every focus period, planned or not (Gap 89 Slice B, A1): Gap 114 makes every
        // spontaneous session a walk-in Focus block anyway.
        if (value > FocusBlock.MaxLength)
            throw new ArgumentException($"Duration cannot exceed {FocusBlock.MaxLength.TotalHours} hours.");

        Value = value;
    }

    public static SessionDuration FromHoursAndMinutes(int hours, int minutes)
        => new SessionDuration(new TimeSpan(hours, minutes, 0));
}