namespace NenFocus.Application.Interfaces;

/// <summary>
/// A slow "screen refresh" heartbeat — the Planner repositions its "now" line on each tick,
/// and a debug-clock jump raises one too. Distinct from <see cref="ISystemClock"/> (the one
/// source of "now" and the user's time zone): a refresh cadence is a UI concern, not time
/// knowledge. Its name is Gap 89's to settle (Gap 88 removed only <c>LocalNow</c>).
/// </summary>
public interface IPlannerClockService
{
    event EventHandler? Tick;
}
