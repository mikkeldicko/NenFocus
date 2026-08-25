namespace NenFocus.Application.Interfaces;

public static class SystemClockExtensions
{
    /// <summary>Local calendar day a UTC instant falls on — the common case built on <see cref="ISystemClock.ToLocal"/>.</summary>
    public static DateOnly ToLocalDate(this ISystemClock clock, DateTime utc) =>
        DateOnly.FromDateTime(clock.ToLocal(utc));
}
