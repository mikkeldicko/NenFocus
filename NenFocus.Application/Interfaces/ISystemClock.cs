using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// The one source of "now" and of the user's time zone (Gap 88). A caller that needs a
/// calendar time asks <see cref="TimeZone"/>; it never reads the machine clock or converts itself.
/// </summary>
public interface ISystemClock
{
    /// <summary>The current moment.</summary>
    Moment Now { get; }

    /// <summary>
    /// The user's time zone — every moment ↔ calendar-time conversion goes through it.
    /// </summary>
    UserTimeZone TimeZone { get; }
}
