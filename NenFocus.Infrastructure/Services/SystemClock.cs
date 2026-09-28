using NenFocus.Application.Interfaces;
using System.Timers;
using Timer = System.Timers.Timer;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// The app's single source of "now". Normally a thin pass-through to
/// <see cref="DateTime.UtcNow"/>; <see cref="IDebugClockController"/> lets a debug UI
/// apply a standing offset (a "jump ahead") on top of real elapsed time, so every
/// consumer of <see cref="ISystemClock"/> — heartbeats, session timers, block windows —
/// observes the jump on its next read with no code changes of its own.
/// </summary>
public sealed class SystemClock : ISystemClock, IDebugClockController, IDisposable
{
    private readonly Timer _tickTimer;
    private TimeSpan _offset = TimeSpan.Zero;

    public DateTime UtcNow => DateTime.UtcNow + _offset;
    public bool IsOverridden => _offset != TimeSpan.Zero;

    public DateTime ToLocal(DateTime utc) => utc.ToLocalTime();

    // SpecifyKind: a local value computed from a contract window or date arithmetic may carry
    // Kind.Unspecified, and ToUniversalTime would leave a Kind.Utc value unconverted.
    // ToUniversalTime never throws on a DST-skipped local time, unlike ConvertTimeToUtc.
    public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    public event EventHandler? Changed;

    public SystemClock()
    {
        // Drives the debug overlay's live display only — no other consumer subscribes.
        _tickTimer = new Timer(1000) { AutoReset = true };
        _tickTimer.Elapsed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _tickTimer.Start();
    }

    public void SetTo(DateTime utc)
    {
        _offset = utc - DateTime.UtcNow;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AdvanceBy(TimeSpan amount)
    {
        _offset += amount;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _offset = TimeSpan.Zero;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _tickTimer.Dispose();
}
