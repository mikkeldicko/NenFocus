using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;


namespace NenFocus.ViewModels.Pages;

public partial class MainPageViewModel : ViewModelBase
{
    private readonly Action _onStartRitual;
    private readonly Action<PostSessionDto> _onShowPostSession;
    private readonly ISessionStateService _sessionState;
    private readonly IActiveSessionMonitor _monitor;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly ISystemClock _clock;
    private Guid _activeSessionId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRitualCommand))]
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
    private bool _isOnBreak;

    [ObservableProperty]
    private bool _keepsBlockingDuringBreak;

    [ObservableProperty]
    private TimeSpan _breakTimeRemaining;

    [ObservableProperty]
    private double _breakProgress;

    [ObservableProperty]
    private int _breaksTaken;

    [ObservableProperty]
    private bool _isRitualBannerVisible;

    /// <summary>Ready-to-bind marker positions for the session progress bar — the session's own
    /// <c>SessionTimeline.Marks</c> (Gap 92). Empty for "No timer".</summary>
    [ObservableProperty]
    private IReadOnlyList<BreakMark> _breakMarkers = [];

    public string Greeting => _clock.TimeZone.ToCalendarTime(_clock.Now).Hour switch
    {
        < 12 => "Good morning.",
        < 17 => "Good afternoon.",
        _ => "Good evening."
    };

    public DateTime Today => _clock.TimeZone.ToCalendarTime(_clock.Now);

    public ObservableCollection<ChecklistItemViewModel> TodaysChecklist { get; } = new();
    public bool HasNoChecklist => TodaysChecklist.Count == 0;

    public MainPageViewModel(
        Action onStartRitual,
        Action<PostSessionDto> onShowPostSession,
        ISessionStateService sessionState,
        IActiveSessionMonitor monitor,
        ICommandDispatcher commandDispatcher,
        ISystemClock clock)
    {
        _onStartRitual = onStartRitual;
        _onShowPostSession = onShowPostSession;
        _sessionState = sessionState;
        _monitor = monitor;
        _commandDispatcher = commandDispatcher;
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

    // One reading per tick, delivered with the event — the eight values below always come from the
    // same second (Gap 92, A4: they used to be read one by one while the timer moved on).
    private void OnMonitorTick(object? sender, TimelineReading reading) =>
        Dispatcher.UIThread.Post(() => ShowReading(reading));

    private void ShowReading(TimelineReading reading)
    {
        SessionTimeRemaining = reading.TimeRemaining;
        SessionProgress = reading.Progress;
        IsOnBreak = reading.IsOnBreak;
        KeepsBlockingDuringBreak = reading.KeepsBlockingDuringBreak;
        BreakTimeRemaining = reading.BreakTimeRemaining;
        BreakProgress = reading.BreakProgress;
        BreaksTaken = reading.BreaksTaken;
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
        BreakMarkers = session.Timeline?.Marks ?? [];
        ShowReading(_monitor.Current);
    }

    private void ClearSession()
    {
        HasActiveSession = false;
        ShowReading(TimelineReading.None);
        SessionGoal = string.Empty;
        ActivePresetName = string.Empty;
        BreakMarkers = [];
    }

    [RelayCommand]
    private async Task EndSession()
    {
        var result = await _commandDispatcher.DispatchAsync(new EndSessionCommand { SessionId = _activeSessionId });
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
}

