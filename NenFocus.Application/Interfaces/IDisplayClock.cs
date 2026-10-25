namespace NenFocus.Application.Interfaces;

/// <summary>
/// The screen's paces (Gap 89 Slice A): a second for a live clock face (the debug overlay), a minute
/// for the Planner's now-line and day badges. Both are also raised the moment the debug clock jumps,
/// so nothing waits for its next tick. A refresh pace, not time knowledge — "what time is it" is
/// <see cref="ISystemClock"/>'s alone. Separate from the heartbeat (background work every 5 s) and
/// the session countdown (<see cref="IActiveSessionMonitor"/>, which also acts on what it reads).
/// </summary>
public interface IDisplayClock
{
    event EventHandler? SecondTicked;
    event EventHandler? MinuteTicked;
}
