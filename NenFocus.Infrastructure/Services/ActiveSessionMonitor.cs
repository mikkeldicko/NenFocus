using Microsoft.Extensions.Logging;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using System.Timers;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Owns the 1-second countdown timer, keeping all timer logic out of ViewModels. Holds
/// no break state of its own (Gap 63): every tick it asks the session's SessionTimeline
/// where it is (Gap 92, A4), so nothing can get stuck or drift — a missed tick, sleep or a
/// clock jump is simply re-read on the next one. Its only memory is a cursor ("when did
/// I last look") so it can ask which break boundaries were crossed since, and hand
/// those to IBreakTransitionResponder. It ends nothing: when a reading says the session's time
/// is up, the heartbeat hears it and auto-ends the session (Gap 89 Slice A).
/// </summary>
public sealed class ActiveSessionMonitor : IActiveSessionMonitor, IDisposable
{
    private readonly ISystemClock _clock;
    private readonly IBreakTransitionResponder _breakTransitions;
    private readonly ILogger<ActiveSessionMonitor> _logger;
    private readonly object _gate = new();
    private System.Timers.Timer? _timer;

    private Guid? _sessionId;
    private SessionTimeline? _timeline;
    private Moment _lastCheckedAt;

    public TimelineReading Current { get; private set; } = TimelineReading.None;

    public event EventHandler<TimelineReading>? Tick;

    public ActiveSessionMonitor(
        IActiveSessionState sessionState, ISystemClock clock,
        IBreakTransitionResponder breakTransitions, ILogger<ActiveSessionMonitor> logger)
    {
        _clock = clock;
        _breakTransitions = breakTransitions;
        _logger = logger;
        sessionState.SessionChanged += OnSessionChanged;

        // IActiveSessionState.RefreshAsync() runs once, awaited, during app
        // startup (Program.Main) before this monitor is ever
        // constructed. Its SessionChanged notification for an already-active session
        // fired before the subscription above existed to hear it, and unlike a plain
        // UI refresh, this monitor only ever populates _timeline
        // and starts its countdown timer inside OnSessionChanged — missing that one
        // notification means the timer never starts at all, not just a stale display.
        // Catch up directly on whatever session is already active.
        OnSessionChanged(this, sessionState.ActiveSession);
    }

    private void OnSessionChanged(object? sender, SessionDto? session)
    {
        StopTimer();

        lock (_gate)
        {
            if (session?.Timeline is null)
            {
                _sessionId = null;
                _timeline = null;
                Current = TimelineReading.None;
                return;
            }

            // A fresh session (or one rehydrated at startup) starts watching from now:
            // a boundary crossed while the app wasn't looking isn't announced late, the
            // screen just shows the right phase. The same session re-published keeps its
            // cursor, so a boundary between the last tick and now isn't lost.
            if (_sessionId != session.Id)
                _lastCheckedAt = _clock.Now;

            _sessionId = session.Id;
            // Taken on every SessionChanged, not once per session, so any future mid-session
            // revision of the schedule is picked up with no change here.
            _timeline = session.Timeline;

            Recalculate();
        }

        StartTimer();
    }

    private void StartTimer()
    {
        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
        _timer = null;
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        TimelineReading reading;
        lock (_gate)
        {
            if (_sessionId is null) return;
            Recalculate();
            reading = Current;
        }
        Tick?.Invoke(this, reading);
    }

    // Caller holds _gate — System.Timers ticks can overlap, and the cursor must only
    // ever move forward from one reader at a time.
    private void Recalculate()
    {
        if (_timeline is not { } timeline) return; // no session — nothing to count down
        var now = _clock.Now;

        Current = timeline.ReadingAt(now);
        // Its time is up: stop counting. This tick's reading says so, and the heartbeat, hearing it,
        // ends the session at once (Gap 89 Slice A); a failed end is retried at its next beat.
        if (Current.HasEnded)
        {
            StopTimer();
            return;
        }

        var transitions = timeline.TransitionsBetween(_lastCheckedAt, now);
        _lastCheckedAt = now;
        if (transitions.Count > 0)
            _ = HandleBreakTransitionsAsync(transitions, now, timeline.KeepsBlockingDuringBreaks);
    }

    // Fire-and-forget. Nothing needs retrying on failure: blocking is already correct on
    // the heartbeat's next SyncAsync, which reads the same schedule; only the notification
    // for this one boundary would be lost, and that is logged.
    private async Task HandleBreakTransitionsAsync(
        IReadOnlyList<BreakTransition> transitions, Moment at, bool keepBlockingDuringBreaks)
    {
        try
        {
            await _breakTransitions.RespondAsync(transitions, at, keepBlockingDuringBreaks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling break transitions failed for session {SessionId}", _sessionId);
        }
    }

    public void Dispose() => StopTimer();
}
