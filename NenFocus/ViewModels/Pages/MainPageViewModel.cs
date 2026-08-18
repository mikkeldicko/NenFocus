using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.StartBreak;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;


namespace NenFocus.ViewModels.Pages;

public partial class MainPageViewModel : ViewModelBase
{
    private readonly Action _onStartRitual;
    private readonly Action<PostSessionDto> _onShowPostSession;
    private readonly ISessionStateService _sessionState;
    private readonly IActiveSessionMonitor _monitor;
    private readonly ICommandHandler<EndSessionCommand, EndSessionResult> _endSession;
    private readonly ICommandHandler<StartBreakCommand, StartBreakResult> _startBreak;
    private readonly ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> _resumeFromBreak;
    private readonly IPlannerClockService _clock;
    private Guid _activeSessionId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRitualCommand))]
    [NotifyPropertyChangedFor(nameof(CanTakeBreak))]
    private bool _hasActiveSession;

    [ObservableProperty]
    private TimeSpan _sessionTimeRemaining;

    [ObservableProperty]
    private double _sessionProgress;

    [ObservableProperty]
    private string _sessionGoal = string.Empty;

    [ObservableProperty]
    private string _activePresetName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTakeBreak))]
    private bool _isOnBreak;

    [ObservableProperty]
    private TimeSpan _breakTimeRemaining;

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

    public string Greeting => _clock.LocalNow.Hour switch
    {
        < 12 => "Good morning.",
        < 17 => "Good afternoon.",
        _ => "Good evening."
    };

    public DateTime Today => _clock.LocalNow;

    public ObservableCollection<ChecklistItemViewModel> TodaysChecklist { get; } = new();
    public bool HasNoChecklist => TodaysChecklist.Count == 0;

    public MainPageViewModel(
        Action onStartRitual,
        Action<PostSessionDto> onShowPostSession,
        ISessionStateService sessionState,
        IActiveSessionMonitor monitor,
        ICommandHandler<EndSessionCommand, EndSessionResult> endSession,
        ICommandHandler<StartBreakCommand, StartBreakResult> startBreak,
        ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> resumeFromBreak,
        IPlannerClockService clock)
    {
        _onStartRitual = onStartRitual;
        _onShowPostSession = onShowPostSession;
        _sessionState = sessionState;
        _monitor = monitor;
        _endSession = endSession;
        _startBreak = startBreak;
        _resumeFromBreak = resumeFromBreak;
        _clock = clock;

        _sessionState.RitualRequirementChanged += (_, change) =>
            Dispatcher.UIThread.Post(() => IsRitualBannerVisible = change.Required);

        _sessionState.SessionChanged += OnSessionChanged;

        _monitor.Tick += OnMonitorTick;
        _monitor.SessionAutoEnded += OnSessionAutoEnded;

        // SessionStateService.InitializeAsync() now runs once, awaited, during app
        // startup (Program.Main) before this ViewModel is ever constructed — calling it
        // again here would re-run the DB read and the missed-focus-block sweep a second
        // time for nothing. Catch up directly on whatever it already loaded instead,
        // since the SessionChanged event it fires already happened before the
        // subscription above existed to hear it.
        OnSessionChanged(this, _sessionState.ActiveSession);
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

    private void OnMonitorTick(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SessionTimeRemaining = _monitor.SessionTimeRemaining;
            SessionProgress = _monitor.SessionProgress;
            BreakTimeRemaining = _monitor.BreakTimeRemaining;
            BreakProgress = _monitor.BreakProgress;
        });
    }

    private void OnSessionAutoEnded(object? sender, PostSessionDto postSession)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ClearSession();
            _onShowPostSession(postSession);
        });
    }

    private void ApplySession(SessionDto session)
    {
        _activeSessionId = session.Id;
        SessionGoal = session.Goal;
        ActivePresetName = session.PresetName;
        HasActiveSession = true;
        HasBreakSettings = session.BreakSettings is not null;
        BreaksTaken = session.BreaksTaken;
        IsOnBreak = session.Status == SessionStatus.OnBreak;
    }

    private void ClearSession()
    {
        HasActiveSession = false;
        IsOnBreak = false;
        HasBreakSettings = false;
        SessionTimeRemaining = TimeSpan.Zero;
        BreakTimeRemaining = TimeSpan.Zero;
        SessionProgress = 0;
        BreakProgress = 0;
        BreaksTaken = 0;
        SessionGoal = string.Empty;
        ActivePresetName = string.Empty;
    }

    [RelayCommand]
    private async Task EndSession()
    {
        var result = await _endSession.HandleAsync(new EndSessionCommand { SessionId = _activeSessionId });
        if (result.Success && result.PostSession is not null)
            _onShowPostSession(result.PostSession);
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
    private async Task ResumeFromBreak()
    {
        await _resumeFromBreak.HandleAsync(new ResumeFromBreakCommand { SessionId = _activeSessionId });
    }
}

