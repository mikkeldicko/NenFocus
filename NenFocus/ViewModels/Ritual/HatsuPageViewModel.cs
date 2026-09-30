using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAvailableBreakTimerPresets;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Application.UseCases.Sessions.StartSession;
using System.Linq;

namespace NenFocus.ViewModels.Ritual;

public partial class HatsuPageViewModel : ViewModelBase
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ISystemClock _clock;
    private readonly Func<HatsuData> _getData;
    private readonly Action _onSessionStarted;
    private readonly Action _onBack;

    public FocusBlockSchedule? PlannedSchedule { get; }
    public bool IsPlannedMode => PlannedSchedule is not null;

    // Raw local-time values only — actual "HH:mm"/"1h 30m" string formatting happens in
    // DateTimeToStringConverter/MinutesToStringConverter in the View, not here (ViewModel
    // Forbidden Zone — no display formatting in a ViewModel). Converting through the user's time
    // zone is a data conversion, not formatting (Gap 88), as RitualShellViewModel does for its
    // late-block Start/End.
    public DateTime? PlannedRangeStartLocal => PlannedSchedule is { } s ? _clock.TimeZone.ToCalendarTimes(s.Slot).Start : null;
    public DateTime? PlannedRangeEndLocal => PlannedSchedule is { } s ? _clock.TimeZone.ToCalendarTimes(s.Slot).End : null;

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
        ? PlannedSchedule!.RemainingMinutes(_clock.Now)
        : SelectedHours * 60 + SelectedMinutes;
    public bool ShowZeroError => !IsPlannedMode && TotalMinutes == 0;

    /// <summary>
    /// Only presets whose BreakTimer.ScheduleFor(current duration) actually produces
    /// a real break are offered — see GetAvailableBreakTimerPresetsQuery. Recomputed
    /// whenever the duration changes (Spontaneous mode's Hours/Minutes wheel); a
    /// Planned session's duration never changes mid-Hatsu, so one initial load
    /// suffices there.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreakTimerPresetChips))]
    private IReadOnlyList<BreakTimerPresetDto> _availableBreakTimerPresets = [];

    /// <summary>Null means "No timer" — the session runs with no breaks at all.
    /// Manual break-taking is retired app-wide; this is the only break configuration
    /// left, replacing the old free-form interval/length picker entirely.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreakTimerPresetChips))]
    private BreakTimerPresetDto? _selectedBreakTimerPreset;

    /// <summary>
    /// AvailableBreakTimerPresets paired with whether each one is currently selected —
    /// exists purely so the View can bind a chip's "active" style directly off its own
    /// DataContext (IsSelected) instead of comparing two different DataContexts across
    /// an ItemsControl boundary in XAML.
    /// </summary>
    public IReadOnlyList<BreakTimerPresetChip> BreakTimerPresetChips =>
        AvailableBreakTimerPresets
            .Select(p => new BreakTimerPresetChip(p, p.Id == SelectedBreakTimerPreset?.Id))
            .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isStarting;

    [ObservableProperty]
    private string? _errorMessage;

    public bool CanStart => IsPlannedMode
        ? !PlannedSchedule!.HasEnded(_clock.Now) && !IsStarting
        : TotalMinutes > 0 && TotalMinutes <= 480 && !IsStarting;

    public HatsuPageViewModel(
        ICommandDispatcher commandDispatcher,
        IQueryDispatcher queryDispatcher,
        ISystemClock clock,
        Func<HatsuData> getData,
        Action onSessionStarted,
        Action onBack,
        FocusBlockSchedule? plannedSchedule = null)
    {
        _commandDispatcher = commandDispatcher;
        _queryDispatcher = queryDispatcher;
        _clock = clock;
        _getData = getData;
        _onSessionStarted = onSessionStarted;
        _onBack = onBack;
        PlannedSchedule = plannedSchedule;

        _ = RefreshAvailableBreakTimerPresetsAsync();
    }

    /// <summary>
    /// Recomputes which presets are offered for the duration currently in play — a
    /// preset that no longer fits after a duration change is deselected rather than
    /// left silently selected but hidden.
    /// </summary>
    private async Task RefreshAvailableBreakTimerPresetsAsync()
    {
        if (TotalMinutes <= 0)
        {
            AvailableBreakTimerPresets = [];
            SelectedBreakTimerPreset = null;
            return;
        }

        var available = await _queryDispatcher.QueryAsync(
            new GetAvailableBreakTimerPresetsQuery(TimeSpan.FromMinutes(TotalMinutes)));

        AvailableBreakTimerPresets = available;

        if (SelectedBreakTimerPreset is { } current && available.All(p => p.Id != current.Id))
            SelectedBreakTimerPreset = null;
    }

    [RelayCommand]
    private void SelectBreakTimerPreset(BreakTimerPresetDto? preset)
    {
        SelectedBreakTimerPreset = preset;
    }

    [RelayCommand]
    private void SelectHours(string param)
    {
        if (!int.TryParse(param, out var hours)) return;
        SelectedHours = hours;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ShowZeroError));
        _ = RefreshAvailableBreakTimerPresetsAsync();
    }

    [RelayCommand]
    private void SelectMinutes(string param)
    {
        if (!int.TryParse(param, out var minutes)) return;
        SelectedMinutes = minutes;
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ShowZeroError));
        _ = RefreshAvailableBreakTimerPresetsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartSession()
    {
        ErrorMessage = null;

        if (TotalMinutes == 0) return;

        var data = _getData();
        var command = new StartSessionCommand
        {
            Goal = data.Goal,
            PresetId = data.PresetId,
            DurationMinutes = TotalMinutes,
            BreakTimerPresetId = SelectedBreakTimerPreset?.Id,
            OriginatingBlockId = data.OriginatingBlockId,
            AdditionalBlockedSites = data.AdditionalBlockedSites,
            AdditionalBlockedApps = data.AdditionalBlockedApps
        };

        IsStarting = true;
        try
        {
            var result = await _commandDispatcher.DispatchAsync(command);

            if (result.Success)
                _onSessionStarted();
            else
                ErrorMessage = result.ErrorMessage ?? "Failed to start session.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsStarting = false;
        }
    }

    [RelayCommand]
    private void Back() => _onBack();
}
public sealed record HatsuData
{
    public string Goal { get; init; } = string.Empty;
    public Guid? PresetId { get; init; }
    public Guid? OriginatingBlockId { get; init; }
    public IReadOnlyList<string> AdditionalBlockedSites { get; init; } = [];
    public IReadOnlyList<string> AdditionalBlockedApps { get; init; } = [];
}

public sealed record BreakTimerPresetChip(BreakTimerPresetDto Preset, bool IsSelected)
{
    public string Name => Preset.Name;
}
