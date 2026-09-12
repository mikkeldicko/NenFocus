using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.StartBreak;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using System.Timers;

namespace NenFocus.Infrastructure.Services;

/// <summary>Owns the 1-second countdown timer, keeping all timer logic out of ViewModels.</summary>
public sealed class ActiveSessionMonitor : IActiveSessionMonitor, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemClock _clock;
    private readonly IBreakScheduleCalculator _scheduleCalculator;
    private readonly ILogger<ActiveSessionMonitor> _logger;
    private System.Timers.Timer? _timer;

    private Guid _sessionId;
    private DateTime _sessionStart;
    private DateTime _sessionEnd;
    private double _sessionDurationSeconds;
    private DateTime? _breakStartedAt;
    private double _effectiveBreakSeconds;
    private bool _autoEndPending;

    /// <summary>The full phase timeline for this session — empty when BreakTimer is
    /// null ("No timer"). Computed once per session, walked every tick to notice a
    /// Work phase has elapsed and drive the automatic transition into a break.</summary>
    private IReadOnlyList<SessionPhase> _schedule = [];
    private bool _autoBreakPending;

    public TimeSpan SessionTimeRemaining { get; private set; } = TimeSpan.Zero;
    public double SessionProgress { get; private set; }
    public TimeSpan BreakTimeRemaining { get; private set; } = TimeSpan.Zero;
    public double BreakProgress { get; private set; }
    public IReadOnlyList<BreakMarkerDto> BreakMarkers { get; private set; } = [];

    public event EventHandler? Tick;
    public event EventHandler<PostSessionDto>? SessionAutoEnded;

    public ActiveSessionMonitor(
        ISessionStateService sessionState, IServiceScopeFactory scopeFactory, ISystemClock clock,
        IBreakScheduleCalculator scheduleCalculator, ILogger<ActiveSessionMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _scheduleCalculator = scheduleCalculator;
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
        _autoEndPending = false;
        _autoBreakPending = false;

        if (session is null)
        {
            _schedule = [];
            BreakMarkers = [];
            return;
        }

        _sessionId = session.Id;
        _sessionStart = session.StartedAt;
        _sessionEnd = session.EndAt;
        _sessionDurationSeconds = (session.EndAt - session.StartedAt).TotalSeconds;

        if (session.BreakTimer is { } cadenceDto)
        {
            var cadence = new BreakTimer(
                cadenceDto.WorkMinutes,
                cadenceDto.ShortBreakMinutes,
                cadenceDto.CyclesBeforeLongBreak,
                cadenceDto.LongBreakMinutes);
            _schedule = _scheduleCalculator.ComputeSchedule(cadence, session.EndAt - session.StartedAt);
            BreakMarkers = _schedule
                .Where(p => p.Kind != SessionPhaseKind.Work)
                .Select(p => new BreakMarkerDto
                {
                    ProgressFraction = p.Start.TotalSeconds / _sessionDurationSeconds,
                    IsLongBreak = p.Kind == SessionPhaseKind.LongBreak
                })
                .ToList();
        }
        else
        {
            _schedule = [];
            BreakMarkers = [];
        }

        if (session.Status == SessionStatus.OnBreak && session.BreakStartedAt.HasValue)
        {
            _breakStartedAt = session.BreakStartedAt.Value;
            var sessionRemainingAtBreakStart = (_sessionEnd - _breakStartedAt.Value).TotalSeconds;
            var breakMinutes = session.CurrentBreakKind == SessionPhaseKind.LongBreak
                ? session.BreakTimer?.LongBreakMinutes ?? 5
                : session.BreakTimer?.ShortBreakMinutes ?? 5;
            _effectiveBreakSeconds = Math.Min(breakMinutes * 60.0, Math.Max(0, sessionRemainingAtBreakStart));
        }
        else
        {
            _breakStartedAt = null;
            _effectiveBreakSeconds = 0;
        }

        Recalculate();
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
        Recalculate();
        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void Recalculate()
    {
        var remaining = _sessionEnd - _clock.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            SessionTimeRemaining = TimeSpan.Zero;
            SessionProgress = 1.0;
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

        // Walk the precomputed phase timeline to notice a Work phase has elapsed —
        // manual break-taking is retired, this is now the only path into OnBreak.
        // Guarded on !_breakStartedAt so a session already on break (rehydrated from
        // the DB, or one this same check already triggered) is never re-triggered.
        if (_schedule.Count > 0 && !_breakStartedAt.HasValue && !_autoBreakPending)
        {
            var elapsed = _clock.UtcNow - _sessionStart;
            var currentPhase = _schedule.FirstOrDefault(p => elapsed >= p.Start && elapsed < p.End);
            if (currentPhase is { Kind: SessionPhaseKind.ShortBreak or SessionPhaseKind.LongBreak })
            {
                _autoBreakPending = true;
                _ = AutoStartBreakAsync(currentPhase.Kind);
            }
        }

        if (_breakStartedAt.HasValue)
        {
            var breakEnd = _breakStartedAt.Value.AddSeconds(_effectiveBreakSeconds);
            var breakRemaining = breakEnd - _clock.UtcNow;

            if (breakRemaining <= TimeSpan.Zero)
            {
                BreakTimeRemaining = TimeSpan.Zero;
                BreakProgress = 1.0;
                _breakStartedAt = null;
                _ = AutoResumeBreakAsync();
                return;
            }

            BreakTimeRemaining = breakRemaining;
            BreakProgress = Math.Clamp(1.0 - (breakRemaining.TotalSeconds / _effectiveBreakSeconds), 0.0, 1.0);
        }
        else
        {
            BreakTimeRemaining = TimeSpan.Zero;
            BreakProgress = 0;
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
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<EndSessionCommand, EndSessionResult>>();
            var result = await handler.HandleAsync(new EndSessionCommand { SessionId = _sessionId });
            if (result.Success && result.PostSession is not null)
                SessionAutoEnded?.Invoke(this, result.PostSession);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-end failed for session {SessionId}; retrying next tick", _sessionId);
            _autoEndPending = false;
            StartTimer();
        }
    }

    // Fire-and-forget, same reasoning as AutoEndAsync above: an unhandled exception
    // here must not silently strand the session in Active forever with no break ever
    // firing. On failure, _autoBreakPending resets so the next tick retries — the
    // schedule is deterministic, so retrying is always safe and idempotent in effect
    // (StartBreakCommandHandler itself also guards Status == Active).
    private async Task AutoStartBreakAsync(SessionPhaseKind breakKind)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<StartBreakCommand, StartBreakResult>>();
            await handler.HandleAsync(new StartBreakCommand { SessionId = _sessionId, BreakKind = breakKind });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-start-break failed for session {SessionId}", _sessionId);
            _autoBreakPending = false;
        }
    }

    private async Task AutoResumeBreakAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>>();
            await handler.HandleAsync(new ResumeFromBreakCommand { SessionId = _sessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-resume-from-break failed for session {SessionId}", _sessionId);
        }
    }

    public void Dispose() => StopTimer();
}
