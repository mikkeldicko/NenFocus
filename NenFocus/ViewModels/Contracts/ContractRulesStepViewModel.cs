using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Contracts;

public partial class SimpleContractOptionViewModel : ViewModelBase
{
    public string Name { get; }
    public string MentalModel { get; }
    public StrictnessRule StrictnessRule { get; }
    public bool AllowCancellation { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SimpleContractOptionViewModel(string name, string mentalModel, StrictnessRule strictnessRule, bool allowCancellation)
    {
        Name = name;
        MentalModel = mentalModel;
        StrictnessRule = strictnessRule;
        AllowCancellation = allowCancellation;
    }
}

public partial class RestDayChipViewModel : ViewModelBase
{
    public DateOnly Date { get; }

    [ObservableProperty]
    private bool _isRestDay;

    public RestDayChipViewModel(DateOnly date) => Date = date;
}

public partial class ContractRulesStepViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>? _getPresets;
    private readonly Action _onContinue;
    private readonly Action _onBack;

    public bool IsAdvanced { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }

    // ── Simple ───────────────────────────────────────────────────────────────
    public ObservableCollection<SimpleContractOptionViewModel> SimpleOptions { get; } = new();

    [ObservableProperty]
    private SimpleContractOptionViewModel? _selectedSimpleOption;

    // ── Advanced ─────────────────────────────────────────────────────────────
    public IReadOnlyList<StrictnessRule> StrictnessOptions { get; } = Enum.GetValues<StrictnessRule>();

    [ObservableProperty] private StrictnessRule _strictnessRule = StrictnessRule.DailyFlexibility;
    [ObservableProperty] private bool _isDailyFloorEnabled;
    [ObservableProperty] private string _dailyFloorHoursText = "3";
    [ObservableProperty] private bool _allowCancellation = true;
    [ObservableProperty] private bool _isWorkHoursEnabled;
    [ObservableProperty] private string _workHoursStartText = "09:00";
    [ObservableProperty] private string _workHoursEndText = "17:00";
    [ObservableProperty] private bool _saveAsPreset;
    [ObservableProperty] private string _presetNameToSave = string.Empty;
    [ObservableProperty] private ContractPresetDto? _selectedStartFromPreset;
    [ObservableProperty] private bool _isLoadingPresets;

    public ObservableCollection<string> TimeOptions { get; } = new();
    public ObservableCollection<RestDayChipViewModel> RestDayChips { get; } = new();
    public ObservableCollection<ContractPresetDto> SavedPresets { get; } = new();

    [ObservableProperty] private string? _error;

    public ContractRulesStepViewModel(
        bool isAdvanced,
        DateOnly startDate,
        DateOnly endDate,
        IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>>? getPresets,
        Action onContinue,
        Action onBack,
        StrictnessRule? initialStrictnessRule = null,
        double? initialFixedDailyFloorHours = null,
        bool initialAllowCancellation = true,
        bool initialIsWorkHoursEnabled = false,
        TimeOnly? initialWorkHoursStart = null,
        TimeOnly? initialWorkHoursEnd = null,
        IReadOnlyCollection<DateOnly>? initialRestDays = null)
    {
        IsAdvanced = isAdvanced;
        StartDate = startDate;
        EndDate = endDate;
        _getPresets = getPresets;
        _onContinue = onContinue;
        _onBack = onBack;

        for (int h = 0; h < 24; h++)
            for (int m = 0; m < 60; m += 15)
                TimeOptions.Add($"{h:D2}:{m:D2}");
        TimeOptions.Add("24:00");

        SimpleOptions.Add(new SimpleContractOptionViewModel("Lockdown", "This schedule is law. I will not touch it.", StrictnessRule.Locked, true));
        SimpleOptions.Add(new SimpleContractOptionViewModel("Daily Flex", "I can shuffle blocks around within each day.", StrictnessRule.DailyFlexibility, true));
        SimpleOptions.Add(new SimpleContractOptionViewModel("Period Study", "Blocks can shift around during the contract, but the total hours I seal stay fixed.", StrictnessRule.PeriodTimeBudget, true));
        SelectedSimpleOption = (!isAdvanced && initialStrictnessRule.HasValue
            ? SimpleOptions.FirstOrDefault(o => o.StrictnessRule == initialStrictnessRule.Value)
            : null) ?? SimpleOptions[0];
        SelectedSimpleOption.IsSelected = true;

        for (var d = startDate; d <= endDate; d = d.AddDays(1))
            RestDayChips.Add(new RestDayChipViewModel(d) { IsRestDay = initialRestDays?.Contains(d) ?? false });

        if (isAdvanced)
        {
            if (initialStrictnessRule.HasValue) StrictnessRule = initialStrictnessRule.Value;
            IsDailyFloorEnabled = initialFixedDailyFloorHours.HasValue;
            if (initialFixedDailyFloorHours.HasValue) DailyFloorHoursText = initialFixedDailyFloorHours.Value.ToString("0.##");
            AllowCancellation = initialAllowCancellation;
            IsWorkHoursEnabled = initialIsWorkHoursEnabled;
            if (initialWorkHoursStart.HasValue) WorkHoursStartText = FormatTime(initialWorkHoursStart.Value);
            if (initialWorkHoursEnd.HasValue) WorkHoursEndText = FormatTime(initialWorkHoursEnd.Value);

            _ = LoadPresetsAsync();
        }
    }

    private async Task LoadPresetsAsync()
    {
        if (_getPresets is null) return;
        IsLoadingPresets = true;
        try
        {
            var presets = await _getPresets.HandleAsync(new GetAllContractPresetsQuery());
            SavedPresets.Clear();
            foreach (var p in presets) SavedPresets.Add(p);
        }
        finally
        {
            IsLoadingPresets = false;
        }
    }

    partial void OnSelectedStartFromPresetChanged(ContractPresetDto? value)
    {
        if (value is null) return;
        StrictnessRule = value.StrictnessRule;
        IsDailyFloorEnabled = value.DailyFloorHours.HasValue;
        if (value.DailyFloorHours.HasValue) DailyFloorHoursText = value.DailyFloorHours.Value.ToString("0.##");
        AllowCancellation = value.AllowCancellation;
        IsWorkHoursEnabled = value.WorkHoursStart.HasValue && value.WorkHoursEnd.HasValue;
        if (value.WorkHoursStart.HasValue) WorkHoursStartText = FormatTime(value.WorkHoursStart.Value);
        if (value.WorkHoursEnd.HasValue) WorkHoursEndText = FormatTime(value.WorkHoursEnd.Value);
    }

    [RelayCommand]
    private void SelectSimpleOption(SimpleContractOptionViewModel option)
    {
        if (SelectedSimpleOption is not null) SelectedSimpleOption.IsSelected = false;
        option.IsSelected = true;
        SelectedSimpleOption = option;
    }

    [RelayCommand]
    private void Continue()
    {
        Error = null;

        if (!IsAdvanced)
        {
            if (SelectedSimpleOption is null) { Error = "Please choose a contract type."; return; }
            _onContinue();
            return;
        }

        // The checkbox is the deliberate on/off switch for "no fixed floor" — mirrors
        // "Enforce work hours" exactly, rather than relying on a blank text field (which
        // looked too similar to typing 0, a mistake that produces a very different,
        // nonsensical result: a floor that's already satisfied by doing nothing).
        // What "no fixed floor" resolves to depends on strictness (Application-side):
        // Daily Flexibility/Locked fall back to a per-day floor derived from whatever's
        // scheduled each day (same as Simple mode); Period Time Budget gets a single
        // whole-period ContractFloor instead.
        double? floor = null;
        if (IsDailyFloorEnabled)
        {
            if (!double.TryParse(DailyFloorHoursText, out var parsedFloor) || parsedFloor % 1 != 0 || parsedFloor < 1 || parsedFloor > 24)
            {
                Error = "Daily floor must be a whole number between 1 and 24 hours.";
                return;
            }
            floor = parsedFloor;
        }

        TimeOnly? workHoursStart = null, workHoursEnd = null;
        if (IsWorkHoursEnabled)
        {
            if (!TryParseTime(WorkHoursStartText, out var start) || !TryParseTime(WorkHoursEndText, out var end) || end <= start)
            {
                Error = "Work hours must have a valid start and end, with end after start.";
                return;
            }
            workHoursStart = start;
            workHoursEnd = end;
        }

        if (floor.HasValue && workHoursStart.HasValue && workHoursEnd.HasValue
            && floor.Value > (workHoursEnd.Value - workHoursStart.Value).TotalHours)
        {
            Error = "Daily floor cannot exceed the total work hours window.";
            return;
        }

        if (SaveAsPreset && string.IsNullOrWhiteSpace(PresetNameToSave))
        {
            Error = "Please name the preset you want to save.";
            return;
        }

        _onContinue();
    }

    [RelayCommand] private void Back() => _onBack();

    // ── Resolved values, pulled by the shell after Continue succeeds ─────────

    public string ResolvedSummaryLabel => IsAdvanced ? "Custom" : $"Simple — {SelectedSimpleOption?.Name}";

    public StrictnessRule ResolvedStrictnessRule => IsAdvanced
        ? StrictnessRule
        : SelectedSimpleOption?.StrictnessRule ?? StrictnessRule.DailyFlexibility;

    public bool ResolvedAllowCancellation => IsAdvanced
        ? AllowCancellation
        : SelectedSimpleOption?.AllowCancellation ?? true;

    public double? ResolvedFixedDailyFloorHours =>
        IsAdvanced && IsDailyFloorEnabled && double.TryParse(DailyFloorHoursText, out var floor) ? floor : null;

    public IReadOnlyList<DateOnly> ResolvedRestDays => IsAdvanced
        ? RestDayChips.Where(c => c.IsRestDay).Select(c => c.Date).ToList()
        : [];

    public TimeOnly? ResolvedWorkHoursStart =>
        IsAdvanced && IsWorkHoursEnabled && TryParseTime(WorkHoursStartText, out var s) ? s : null;

    public TimeOnly? ResolvedWorkHoursEnd =>
        IsAdvanced && IsWorkHoursEnabled && TryParseTime(WorkHoursEndText, out var e) ? e : null;

    public Guid? ResolvedContractPresetId => IsAdvanced ? SelectedStartFromPreset?.Id : null;

    private static bool TryParseTime(string s, out TimeOnly time)
    {
        if (s == "24:00") { time = TimeOnly.MaxValue; return true; }
        return TimeOnly.TryParse(s, out time);
    }

    private static string FormatTime(TimeOnly t) => t.ToString("HH:mm");
}
