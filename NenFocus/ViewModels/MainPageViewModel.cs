using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.StartBreak;

namespace NenFocus.ViewModels;

public partial class MainPageViewModel : ViewModelBase
{
    private readonly Action _onStartRitual;
    private readonly Action<PostSessionDto> _onShowPostSession;
    private readonly ISessionStateService _sessionState;
    private readonly ICommandHandler<EndSessionCommand, EndSessionResult> _endSession;
    private readonly ICommandHandler<StartBreakCommand, StartBreakResult> _startBreak;
    private readonly ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> _resumeFromBreak;
    private DispatcherTimer? _timer;
    private DateTime _sessionEnd;
    private double _sessionDurationSeconds;
    private Guid _activeSessionId;

    private DateTime? _breakStartedAt;
    private int _breakDurationMinutes;
    private double _effectiveBreakSeconds;

    // ── Session state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRitualCommand))]
    [NotifyPropertyChangedFor(nameof(CanTakeBreak))]
    private bool _hasActiveSession;

    [ObservableProperty]
    private string _sessionTimeDisplay = "00:00:00";

    [ObservableProperty]
    private double _sessionProgress;

    [ObservableProperty]
    private string _sessionGoal = string.Empty;

    [ObservableProperty]
    private string _activeProfileName = string.Empty;

    // ── Break state ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTakeBreak))]
    private bool _isOnBreak;

    [ObservableProperty]
    private string _breakTimeDisplay = "00:00";

    [ObservableProperty]
    private double _breakProgress;

    [ObservableProperty]
    private int _breaksTaken;

    [ObservableProperty]
    private bool _isRitualBannerVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTakeBreak))]
    private bool _hasBreakSettings;

    public bool CanTakeBreak => HasActiveSession && !IsOnBreak && HasBreakSettings;

    // ── Header ─────────────────────────────────────────────────────────────

    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning.",
        < 17 => "Good afternoon.",
        _ => "Good evening."
    };

    public string TodayDate => DateTime.Now.ToString("dddd, MMMM d");

    public ObservableCollection<ChecklistItemViewModel> TodaysChecklist { get; } = new();
    public bool HasNoChecklist => TodaysChecklist.Count == 0;

    public MainPageViewModel(
        Action onStartRitual,
        Action<PostSessionDto> onShowPostSession,
        ISessionStateService sessionState,
        ICommandHandler<EndSessionCommand, EndSessionResult> endSession,
        ICommandHandler<StartBreakCommand, StartBreakResult> startBreak,
        ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> resumeFromBreak)
    {
        _onStartRitual = onStartRitual;
        _onShowPostSession = onShowPostSession;
        _sessionState = sessionState;
        _endSession = endSession;
        _startBreak = startBreak;
        _resumeFromBreak = resumeFromBreak;

        _sessionState.RitualRequirementChanged += (s, required) =>
        {
            Dispatcher.UIThread.Post(() => IsRitualBannerVisible = required);
        };

        _sessionState.SessionChanged += OnSessionChanged;

        // Start initialization
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _sessionState.InitializeAsync();
        // 1. Check if the state service already knows about a session
        if (_sessionState.ActiveSession is not null)
        {
            ApplySession(_sessionState.ActiveSession);
        }
        else
        {
            // 2. Fallback: Check the database directly via the state service 
            // (Assumes SessionStateService has a way to refresh from DB)
            if (_sessionState.ActiveSession is not null)
            {
                ApplySession(_sessionState.ActiveSession);
            }
        }
    }

    private void OnSessionChanged(object? sender, SessionDto? session)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (session is not null)
                ApplySession(session);
            else
                ClearSession();
        });
    }

    private void ApplySession(SessionDto session)
    {
        _activeSessionId = session.Id;
        SessionGoal = session.Goal;
        ActiveProfileName = session.ProfileName;

        // Calculate the end time based on when it actually started
        _sessionEnd = session.StartedAt.AddMinutes(session.DurationMinutes);
        _sessionDurationSeconds = session.DurationMinutes * 60.0;

        HasActiveSession = true;

        HasBreakSettings = session.BreakSettings is not null && session.BreakSettings.Enabled;
        _breakDurationMinutes = session.BreakSettings?.BreakDurationMinutes ?? 5;
        BreaksTaken = session.BreaksTaken;

        if (session.Status == "OnBreak" && session.BreakStartedAt.HasValue)
        {
            _breakStartedAt = session.BreakStartedAt.Value;
            var sessionRemainingAtBreakStart = (_sessionEnd - _breakStartedAt.Value).TotalSeconds;
            _effectiveBreakSeconds = Math.Min(_breakDurationMinutes * 60.0, Math.Max(0, sessionRemainingAtBreakStart));
            IsOnBreak = true;
        }
        else
        {
            _breakStartedAt = null;
            _effectiveBreakSeconds = 0;
            IsOnBreak = false;
        }

        StartTimer();
    }

    private void ClearSession()
    {
        StopTimer();
        HasActiveSession = false;
        IsOnBreak = false;
        HasBreakSettings = false;
        SessionTimeDisplay = "00:00:00";
        BreakTimeDisplay = "00:00";
        SessionProgress = 0;
        BreakProgress = 0;
        BreaksTaken = 0;
        SessionGoal = string.Empty;
        ActiveProfileName = string.Empty;
        _breakStartedAt = null;
    }

    private void StartTimer()
    {
        StopTimer();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        UpdateTimerDisplay();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e) => UpdateTimerDisplay();

    private void UpdateTimerDisplay()
    {
        var remaining = _sessionEnd - DateTime.UtcNow;

        // Auto-end session if time is up
        if (remaining <= TimeSpan.Zero)
        {
            StopTimer();
            _ = EndSessionAndShowSummary();
            return;
        }

        SessionTimeDisplay = remaining.ToString(@"hh\:mm\:ss");
        SessionProgress = Math.Clamp(1.0 - (remaining.TotalSeconds / _sessionDurationSeconds), 0, 1);

        if (IsOnBreak && _breakStartedAt.HasValue)
        {
            var breakEnd = _breakStartedAt.Value.AddSeconds(_effectiveBreakSeconds);
            var breakRemaining = breakEnd - DateTime.UtcNow;

            if (breakRemaining <= TimeSpan.Zero)
            {
                _ = ResumeFromBreakAsync();
                return;
            }

            BreakTimeDisplay = breakRemaining.ToString(@"mm\:ss");
            BreakProgress = Math.Clamp(1.0 - (breakRemaining.TotalSeconds / _effectiveBreakSeconds), 0, 1);
        }
    }

    [RelayCommand]
    private async Task EndSession()
    {
        await EndSessionAndShowSummary();
    }

    private async Task EndSessionAndShowSummary()
    {
        var result = await _endSession.HandleAsync(new EndSessionCommand { SessionId = _activeSessionId });
        if (result.Success && result.PostSession is not null)
        {
            _onShowPostSession(result.PostSession);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRitual))]
    private void StartRitual()
    {
        IsRitualBannerVisible = false;
        _onStartRitual();
    }

    private bool CanStartRitual() => !HasActiveSession;

    [RelayCommand]
    private async Task TakeBreak()
    {
        await _startBreak.HandleAsync(new StartBreakCommand { SessionId = _activeSessionId, UserInitiated = true });
    }

    [RelayCommand]
    private async Task ResumeFromBreak() => await ResumeFromBreakAsync();

    private async Task ResumeFromBreakAsync()
    {
        await _resumeFromBreak.HandleAsync(new ResumeFromBreakCommand { SessionId = _activeSessionId });
    }
}