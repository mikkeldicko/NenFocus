using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class DisplayClock : IDisplayClock, IDisposable
{
    private readonly IDebugClockController _debugClock;
    private readonly System.Timers.Timer _everySecond = new(1_000) { AutoReset = true };
    private readonly System.Timers.Timer _everyMinute = new(60_000) { AutoReset = true };

    public event EventHandler? SecondTicked;
    public event EventHandler? MinuteTicked;

    public DisplayClock(IDebugClockController debugClock)
    {
        _debugClock = debugClock;
        _debugClock.Jumped += OnJumped;

        _everySecond.Elapsed += (_, _) => SecondTicked?.Invoke(this, EventArgs.Empty);
        _everyMinute.Elapsed += (_, _) => MinuteTicked?.Invoke(this, EventArgs.Empty);
        _everySecond.Start();
        _everyMinute.Start();
    }

    // A jump shows at once on every pace, instead of up to a minute later.
    private void OnJumped(object? sender, EventArgs e)
    {
        SecondTicked?.Invoke(this, EventArgs.Empty);
        MinuteTicked?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _debugClock.Jumped -= OnJumped;
        _everySecond.Dispose();
        _everyMinute.Dispose();
    }
}
