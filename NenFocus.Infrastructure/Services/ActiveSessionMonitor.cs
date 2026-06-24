using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Domain.Enums;
using System.Timers;

namespace NenFocus.Infrastructure.Services;

/// <summary>Owns the 1-second countdown timer, keeping all timer logic out of ViewModels.</summary>
public sealed class ActiveSessionMonitor : IActiveSessionMonitor, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemClock _clock;
    private System.Timers.Timer? _timer;

    private Guid _sessionId;
    private DateTime _sessionEnd;
    private double _sessionDurationSeconds;
    private DateTime? _breakStartedAt;
    private double _effectiveBreakSeconds;
    private bool _autoEndPending;

    public TimeSpan SessionTimeRemaining { get; private set; } = TimeSpan.Zero;
    public double SessionProgress { get; private set; }
    public TimeSpan BreakTimeRemaining { get; private set; } = TimeSpan.Zero;
    public double BreakProgress { get; private set; }

    public event EventHandler? Tick;
    public event EventHandler<PostSessionDto>? SessionAutoEnded;

    public ActiveSessionMonitor(ISessionStateService sessionState, IServiceScopeFactory scopeFactory, ISystemClock clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        sessionState.SessionChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, SessionDto? session)
    {
        StopTimer();
        _autoEndPending = false;

        if (session is null) return;

        _sessionId = session.Id;
        _sessionEnd = session.StartedAt.AddMinutes(session.DurationMinutes);
        _sessionDurationSeconds = session.DurationMinutes * 60.0;

        if (session.Status == SessionStatus.OnBreak && session.BreakStartedAt.HasValue)
        {
            _breakStartedAt = session.BreakStartedAt.Value;
            var sessionRemainingAtBreakStart = (_sessionEnd - _breakStartedAt.Value).TotalSeconds;
            var breakDuration = (session.BreakSettings?.BreakDurationMinutes ?? 5) * 60.0;
            _effectiveBreakSeconds = Math.Min(breakDuration, Math.Max(0, sessionRemainingAtBreakStart));
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

    private async Task AutoEndAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<EndSessionCommand, EndSessionResult>>();
        var result = await handler.HandleAsync(new EndSessionCommand { SessionId = _sessionId });
        if (result.Success && result.PostSession is not null)
            SessionAutoEnded?.Invoke(this, result.PostSession);
    }

    private async Task AutoResumeBreakAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult>>();
        await handler.HandleAsync(new ResumeFromBreakCommand { SessionId = _sessionId });
    }

    public void Dispose() => StopTimer();
}
