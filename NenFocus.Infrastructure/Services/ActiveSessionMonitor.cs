using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Domain.ValueObjects;
using System.Timers;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Owns the 1-second countdown timer, keeping all timer logic out of ViewModels. Holds
/// no break state of its own (Gap 63): every tick it asks the session's SessionTimeline
/// where it is (Gap 92, A4), so nothing can get stuck or drift — a missed tick, sleep or a
/// clock jump is simply re-read on the next one. Its only memory is a cursor ("when did
/// I last look") so it can ask which break boundaries were crossed since, and hand
/// those to IBreakTransitionResponder.
/// </summary>
public sealed class ActiveSessionMonitor : IActiveSessionMonitor, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemClock _clock;
    private readonly IBreakTransitionResponder _breakTransitions;
    private readonly ILogger<ActiveSessionMonitor> _logger;
    private readonly object _gate = new();
    private System.Timers.Timer? _timer;

    private Guid? _sessionId;
    private SessionTimeline? _timeline;
    private Moment _lastCheckedAt;
    private bool _autoEndPending;

    public TimelineReading Current { get; private set; } = TimelineReading.None;

    public event EventHandler<TimelineReading>? Tick;
    public event EventHandler<PostSessionDto>? SessionAutoEnded;

    public ActiveSessionMonitor(
        ISessionStateService sessionState, IServiceScopeFactory scopeFactory, ISystemClock clock,
        IBreakTransitionResponder breakTransitions, ILogger<ActiveSessionMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _breakTransitions = breakTransitions;
        _logger = logger;
        sessionState.SessionChanged += OnSessionChanged;

        // SessionStateService.InitializeAsync() now runs once, awaited, during app
        // startup (Program.Main) before any DI singleton — including this one — is ever
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
            _autoEndPending = false;

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
        if (Current.HasEnded)
        {
            if (!_autoEndPending)
            {
                _autoEndPending = true;
                StopTimer();
                _ = AutoEndAsync();
            }
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

    // Fire-and-forget (`_ = AutoEndAsync()`) — an unhandled exception here becomes an
    // unobserved task exception: silently discarded, with the countdown already stopped
    // and _autoEndPending already latched true beforehand, so nothing would ever retry
    // and the session would appear to hang at 00:00:00 forever with no error surfaced
    // anywhere. Mirrors ScheduledEnforcementService's heartbeat catch-log-continue
    // precedent, plus re-arming so the next tick retries automatically instead of the
    // failure being permanent.
    private async Task AutoEndAsync()
    {
        var sessionId = _sessionId;
        if (sessionId is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<EndSessionCommand, EndSessionResult>>();
            var result = await handler.HandleAsync(new EndSessionCommand { SessionId = sessionId.Value });
            if (result.Success && result.PostSession is not null)
                SessionAutoEnded?.Invoke(this, result.PostSession);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-end failed for session {SessionId}; retrying next tick", sessionId);
            _autoEndPending = false;
            StartTimer();
        }
    }

    public void Dispose() => StopTimer();
}
