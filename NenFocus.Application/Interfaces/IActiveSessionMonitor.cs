using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// The 1-second clock behind the Main page's countdown. Each tick it asks the running session's
/// <see cref="SessionTimeline"/> where the session is and hands that one reading over — so a reader
/// never mixes values from two seconds, and the screen shows exactly what enforcement reads
/// (Gap 92, A4: it used to re-derive the break answers itself and expose ten separate values).
/// </summary>
public interface IActiveSessionMonitor
{
    /// <summary>The latest reading; <see cref="TimelineReading.None"/> when nothing runs.</summary>
    TimelineReading Current { get; }

    /// <summary>Each second, the new reading — delivered with the event.</summary>
    event EventHandler<TimelineReading>? Tick;
}
