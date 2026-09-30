using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// The one source of "now" and of the user's time zone. Gap 88 moves it to two members —
/// <see cref="Now"/> and <see cref="TimeZone"/>; <see cref="UtcNow"/>, <see cref="ToLocal"/> and
/// <see cref="ToUtc"/> stay beside them while callers convert, and are deleted in phase 6.
/// </summary>
public interface ISystemClock
{
    /// <summary>The current moment. Replaces <see cref="UtcNow"/> (Gap 88).</summary>
    Moment Now { get; }

    /// <summary>
    /// The user's time zone — every moment ↔ calendar-time conversion goes through it.
    /// Replaces <see cref="ToLocal"/>/<see cref="ToUtc"/> (Gap 88).
    /// </summary>
    UserTimeZone TimeZone { get; }

    DateTime UtcNow { get; }

    /// <summary>
    /// Converts a UTC instant to the user's local wall-clock time — the single, testable
    /// seam every "which calendar day is this on" computation must go through. Calling
    /// DateTime.ToLocalTime() directly is untestable (it reads the host OS's real timezone)
    /// and has already produced several silent bugs where a call site simply forgot to
    /// convert at all before deriving a calendar day from a UTC value.
    /// </summary>
    DateTime ToLocal(DateTime utc);

    /// <summary>
    /// Converts the user's local wall-clock time back to a UTC instant — the reverse of
    /// <see cref="ToLocal"/>, through the same seam, so a handler that decides in the
    /// user's frame (Work Hours, "today", quarter-hour slots) hands back a stored-frame
    /// answer without calling DateTime.ToUniversalTime() itself.
    /// </summary>
    DateTime ToUtc(DateTime local);
}
