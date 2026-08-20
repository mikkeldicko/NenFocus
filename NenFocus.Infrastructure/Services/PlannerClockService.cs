using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class PlannerClockService : IPlannerClockService, IDisposable
{
    private readonly ISystemClock _clock;
    private readonly IDebugClockController _debugClock;
    private readonly System.Timers.Timer _timer;
    private readonly EventHandler _onDebugClockChanged;

    public event EventHandler? Tick;
    public DateTime LocalNow => _clock.UtcNow.ToLocalTime();

    public PlannerClockService(ISystemClock clock, IDebugClockController debugClock)
    {
        _clock = clock;
        _debugClock = debugClock;

        // The debug clock's Changed event also fires on its own ~1s ambient ticker, not
        // just on a deliberate jump — harmless here since this only triggers a cheap
        // "now" line reposition, and it means a debug-clock jump is reflected within
        // ~1s instead of waiting for this service's own 60s cadence below.
        _onDebugClockChanged = (_, _) => Tick?.Invoke(this, EventArgs.Empty);
        _debugClock.Changed += _onDebugClockChanged;

        _timer = new System.Timers.Timer(60_000) { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
        _timer.Start();
    }

    public void Dispose()
    {
        _debugClock.Changed -= _onDebugClockChanged;
        _timer.Dispose();
    }
}
