using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class PlannerClockService : IPlannerClockService, IDisposable
{
    private readonly System.Timers.Timer _timer;

    public event EventHandler? Tick;
    public DateTime LocalNow => DateTime.Now;

    public PlannerClockService()
    {
        _timer = new System.Timers.Timer(60_000) { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
        _timer.Start();
    }

    public void Dispose() => _timer.Dispose();
}
