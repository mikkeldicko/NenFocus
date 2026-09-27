using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System.Timers;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Owns the 1-second countdown timer, keeping all timer logic out of ViewModels. Holds
/// no break state of its own (Gap 63): every tick it reads the session's BreakSchedule
/// against the clock, so nothing can get stuck or drift — a missed tick, sleep or a
/// clock jump is simply re-read on the next one. Its only memory is a cursor ("when did
/// I last look") so it can ask which break boundaries were crossed since, and hand
/// those to IBreakTransitionHandler.
/// </summary>
public sealed class ActiveSessionMonitor : IActiveSessionMonitor, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemClock _clock;
    private readonly IBreakTransitionHandler _breakTransitions;
    private readonly ILogger<ActiveSessionMonitor> _logger;
    private readonly object _gate = new();
    private System.Timers.Timer? _timer;

    private Guid? _sessionId;
    private DateTime _sessionStart;
    private DateTime _sessionEnd;
    private double _sessionDurationSeconds;
    private BreakSchedule _schedule = BreakSchedule.None;
    private DateTime _lastCheckedAt;
    private bool _autoEndPending;

    public TimeSpan SessionTimeRemaining { get; private set; } = TimeSpan.Zero;
    public double SessionProgress { get; private set; }
    public bool IsOnBreak { get; private set; }
    public TimeSpan BreakTimeRemaining { get; private set; } = TimeSpan.Zero;
    public double BreakProgress { get; private set; }
    public int BreaksTaken { get; private set; }
    public IReadOnlyList<BreakMarkerDto> BreakMarkers { get; private set; } = [];

    public event EventHandler? Tick;
    public event EventHandler<PostSessionDto>? SessionAutoEnded;

    public ActiveSessionMonitor(
        ISessionStateService sessionState, IServiceScopeFactory scopeFactory, ISystemClock clock,
        IBreakTransitionHandler breakTransitions, ILogger<ActiveSessionMonitor> logger)
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
        // UI refresh, this monitor only ever populates _sessionEnd/_sessionDurationSeconds
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

            if (session is null)
            {
                _sessionId = null;
                _schedule = BreakSchedule.None;
                BreakMarkers = [];
                IsOnBreak = false;
                BreaksTaken = 0;
                BreakTimeRemaining = TimeSpan.Zero;
                BreakProgress = 0;
                return;
            }

            // A fresh session (or one rehydrated at startup) starts watching from now:
            // a boundary crossed while the app wasn't looking isn't announced late, the
            // screen just shows the right phase. The same session re-published keeps its
            // cursor, so a boundary between the last tick and now isn't lost.
            if (_sessionId != session.Id)
                _lastCheckedAt = _clock.UtcNow;

            _sessionId = session.Id;
            _sessionStart = session.StartedAt;
            _sessionEnd = session.EndAt;
            _sessionDurationSeconds = (session.EndAt - session.StartedAt).TotalSeconds;

            // Rebuilt on every SessionChanged, not once per session, so any future
            // mid-session revision of the schedule is picked up with no change here.
            _schedule = new BreakSchedule(session.Breaks.Select(b => new SessionBreak(b.Kind, b.Start, b.End)));
            BreakMarkers = _schedule.Breaks
                .Select(b => new BreakMarkerDto
                {
                    ProgressFraction = b.Start.TotalSeconds / _sessionDurationSeconds,
                    IsLongBreak = b.Kind == SessionPhaseKind.LongBreak
                })
                .ToList();

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
        lock (_gate)
        {
            if (_sessionId is null) return;
            Recalculate();
        }
        Tick?.Invoke(this, EventArgs.Empty);
    }

    // Caller holds _gate — System.Timers ticks can overlap, and the cursor must only
    // ever move forward from one reader at a time.
    private void Recalculate()
    {
        var now = _clock.UtcNow;
        var remaining = _sessionEnd - now;

        if (remaining <= TimeSpan.Zero)
        {
            SessionTimeRemaining = TimeSpan.Zero;
            SessionProgress = 1.0;
            IsOnBreak = false;
            BreakTimeRemaining = TimeSpan.Zero;
            BreakProgress = 0;
            if (!_autoEndPending)
            {
                _autoEndPending = true;
                StopTimer();
                _ = AutoEndAsync();
            }
            return;
        }

        SessionTimeRemaining = remaining;
        SessionProgress = Math.Clamp(1.0 - (remaining.TotalSeconds / _sessionDurationSeconds), 0.0, 1.0);

        var offset = now - _sessionStart;
        var transitions = _schedule.TransitionsBetween(_lastCheckedAt - _sessionStart, offset);
        _lastCheckedAt = now;
        if (transitions.Count > 0)
            _ = HandleBreakTransitionsAsync(transitions, now);

        BreaksTaken = _schedule.BreaksStartedBy(offset);

        if (_schedule.BreakAt(offset) is { } current)
        {
            IsOnBreak = true;
            BreakTimeRemaining = current.End - offset;
            BreakProgress = Math.Clamp((offset - current.Start) / current.Length, 0.0, 1.0);
        }
        else
        {
            IsOnBreak = false;
            BreakTimeRemaining = TimeSpan.Zero;
            BreakProgress = 0;
        }
    }

    // Fire-and-forget. Nothing needs retrying on failure: blocking is already correct on
    // the heartbeat's next SyncAsync, which reads the same schedule; only the notification
    // for this one boundary would be lost, and that is logged.
    private async Task HandleBreakTransitionsAsync(IReadOnlyList<BreakTransition> transitions, DateTime at)
    {
        try
        {
            await _breakTransitions.HandleAsync(transitions, at);
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
