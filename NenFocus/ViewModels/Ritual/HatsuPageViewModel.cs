using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockSchedule;
using NenFocus.Application.UseCases.Sessions.StartSession;

namespace NenFocus.ViewModels.Ritual;

public partial class HatsuPageViewModel : ViewModelBase
{
    private readonly ICommandHandler<StartSessionCommand, StartSessionResult> _startSession;
    private readonly ISystemClock _clock;
    private readonly Func<HatsuData> _getData;
    private readonly Action _onSessionStarted;
    private readonly Action _onBack;

    public FocusBlockSchedule? PlannedSchedule { get; }
    public bool IsPlannedMode => PlannedSchedule is not null;

    // Formatted as "14:00 – 15:00"
    public string PlannedTimeRangeLabel => PlannedSchedule is { } s
        ? $"{s.Start.ToLocalTime():HH:mm} – {s.End.ToLocalTime():HH:mm}"
        : string.Empty;

    // Total block duration (e.g. "1 h", "30 min") — shown for context
    public string PlannedDurationLabel
    {
        get
        {
            if (PlannedSchedule is null) return string.Empty;
            var total = PlannedSchedule.TotalMinutes;
            var h = total / 60;
            var m = total % 60;
            if (h == 0) return $"{m} min";
            if (m == 0) return $"{h} h";
            return $"{h} h {m} min";
        }
    }

    public static int[] HourOptions { get; } = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
    public static int[] MinuteOptions { get; } = { 0, 5, 10, 15, 20, 30, 45 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(ShowZeroError))]
    private int _selectedHours = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(ShowZeroError))]
    private int _selectedMinutes = 30;

    // In planned mode this is evaluated fresh at the moment Start is clicked.
    public int TotalMinutes => IsPlannedMode
        ? Math.Max(1, (int)(PlannedSchedule!.End - _clock.UtcNow).TotalMinutes)
        : SelectedHours * 60 + SelectedMinutes;
    public bool ShowZeroError => !IsPlannedMode && TotalMinutes == 0;

    [ObservableProperty]
    private bool _breakEnabled;

    [ObservableProperty]
    private string _selectedInterval = "25";

    [ObservableProperty]
    private string _selectedBreakLength = "5";

    [ObservableProperty]
    private bool _isCustomInterval;

    [ObservableProperty]
    private bool _isCustomBreakLength;

    [ObservableProperty]
    private string _customIntervalInput = string.Empty;

    [ObservableProperty]
    private string _customBreakLengthInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isStarting;

    [ObservableProperty]
    private string? _errorMessage;

    public bool CanStart => IsPlannedMode
        ? PlannedSchedule!.End > _clock.UtcNow && !IsStarting
        : TotalMinutes > 0 && TotalMinutes <= 480 && !IsStarting;

    public static string[] IntervalOptions { get; } = ["25", "45", "60", "Other"];
    public static string[] BreakLengthOptions { get; } = ["5", "10", "15", "Other"];

    public HatsuPageViewModel(
        ICommandHandler<StartSessionCommand, StartSessionResult> startSession,
        ISystemClock clock,
        Func<HatsuData> getData,
        Action onSessionStarted,
        Action onBack,
        FocusBlockSchedule? plannedSchedule = null)
    {
        _startSession = startSession;
        _clock = clock;
        _getData = getData;
        _onSessionStarted = onSessionStarted;
        _onBack = onBack;
        PlannedSchedule = plannedSchedule;
    }

    [RelayCommand]
    private void SelectHours(string param)
    {
        if (!int.TryParse(param, out var hours)) return;
        SelectedHours = hours;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ShowZeroError));
    }

    [RelayCommand]
    private void SelectMinutes(string param)
    {
        if (!int.TryParse(param, out var minutes)) return;
        SelectedMinutes = minutes;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ShowZeroError));
    }

    [RelayCommand]
    private void SelectInterval(string option)
    {
        SelectedInterval = option;
        IsCustomInterval = option == "Other";
    }

    [RelayCommand]
    private void SelectBreakLength(string option)
    {
        SelectedBreakLength = option;
        IsCustomBreakLength = option == "Other";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartSession()
    {
        ErrorMessage = null;

        if (TotalMinutes == 0) return;

        BreakSettingsDto? breakSettings = null;
        if (BreakEnabled)
        {
            var interval = IsCustomInterval
                ? (int.TryParse(CustomIntervalInput, out var ci) ? ci : 25)
                : int.Parse(SelectedInterval);
            var breakLen = IsCustomBreakLength
                ? (int.TryParse(CustomBreakLengthInput, out var cl) ? cl : 5)
                : int.Parse(SelectedBreakLength);

            breakSettings = new BreakSettingsDto
            {
                Enabled = true,
                IntervalMinutes = interval,
                BreakDurationMinutes = breakLen
            };
        }

        var data = _getData();
        var command = new StartSessionCommand
        {
            Goal = data.Goal,
            ProfileId = data.ProfileId,
            DurationMinutes = TotalMinutes,
            BreakSettings = breakSettings,
            OriginatingBlockId = data.OriginatingBlockId,
            AdditionalBlockedSites = data.AdditionalBlockedSites,
            AdditionalBlockedApps = data.AdditionalBlockedApps
        };

        IsStarting = true;
        var result = await _startSession.HandleAsync(command);
        IsStarting = false;

        if (result.Success)
            _onSessionStarted();
        else
            ErrorMessage = result.ErrorMessage ?? "Failed to start session.";
    }

    [RelayCommand]
    private void Back() => _onBack();
}
public sealed record HatsuData
{
    public string Goal { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    public Guid? OriginatingBlockId { get; init; }
    public IReadOnlyList<string> AdditionalBlockedSites { get; init; } = [];
    public IReadOnlyList<string> AdditionalBlockedApps { get; init; } = [];
}
