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
using NenFocus.Converters;
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

/// <summary>
/// "Advanced" mode no longer means freeform rule entry — rules are authored and edited
/// entirely on the Presets page (see ContractPresetPageViewModel). This step's non-Simple
/// path is a preset picker: whatever the selected ContractPresetDto carries is what the
/// contract gets, applied as-is, with no further tweaking here. Rest days stay here
/// regardless — they're date-bound to this specific contract's range, never a preset concern.
/// </summary>
public partial class ContractRulesStepViewModel : ViewModelBase
{
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly Action _onContinue;
    private readonly Action _onBack;

    // Fallback rules for an existing contract being edited whose values don't (or no longer)
    // match any saved preset — e.g. sealed before Contract Presets existed, or its preset was
    // since deleted. Only ever populated when editing; a brand-new contract has none, so
    // Continue() requires an explicit preset pick in that case.
    private readonly StrictnessRule? _initialStrictnessRule;
    private readonly double? _initialFixedDailyFloorHours;
    private readonly bool _initialAllowCancellation;
    private readonly TimeOnly? _initialWorkHoursStart;
    private readonly TimeOnly? _initialWorkHoursEnd;
    private readonly Guid? _initialContractPresetId;
    private readonly RedemptionPlacementPolicy? _initialRedemptionPlacementPolicy;
    private readonly bool _initialKeepBlockingDuringBreaks;
    private readonly bool _initialBonusBlocksFollowStrictness;

    public bool IsAdvanced { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }

    // ── Simple ───────────────────────────────────────────────────────────────
    public ObservableCollection<SimpleContractOptionViewModel> SimpleOptions { get; } = new();

    [ObservableProperty]
    private SimpleContractOptionViewModel? _selectedSimpleOption;

    // ── Preset pick (formerly "Advanced") ───────────────────────────────────
    [ObservableProperty] private ContractPresetDto? _selectedPreset;
    [ObservableProperty] private bool _isLoadingPresets;

    partial void OnSelectedPresetChanged(ContractPresetDto? value)
    {
        OnPropertyChanged(nameof(SelectedPresetFloorLabel));
        OnPropertyChanged(nameof(SelectedPresetWorkHoursLabel));
        OnPropertyChanged(nameof(SelectedPresetCancellationLabel));
        OnPropertyChanged(nameof(SelectedPresetRedemptionPlacementLabel));
        OnPropertyChanged(nameof(SelectedPresetBreakBlockingLabel));
        OnPropertyChanged(nameof(SelectedPresetBonusBlocksLabel));
    }

    // Read-only confirmation text for the picked preset — shares its formatters with
    // ContractWizardViewModel's own Review-step summary strings, so the two can't drift apart.
    public string SelectedPresetFloorLabel => DailyFloorToLabelConverter.FormatFloor(SelectedPreset?.DailyFloorHours);

    public string SelectedPresetWorkHoursLabel =>
        WorkHoursRangeToLabelConverter.FormatRange(SelectedPreset?.WorkHoursStart, SelectedPreset?.WorkHoursEnd);

    public string SelectedPresetCancellationLabel =>
        (SelectedPreset?.AllowCancellation ?? false) ? "Cancellation allowed" : "No cancellation";

    public string SelectedPresetRedemptionPlacementLabel =>
        SelectedPreset?.RedemptionPlacementPolicy == RedemptionPlacementPolicy.CurrentDayOnly
            ? "Redemption: current day only"
            : "Redemption: anywhere in the contract";

    public string SelectedPresetBreakBlockingLabel =>
        (SelectedPreset?.KeepBlockingDuringBreaks ?? false)
            ? "Blocking stays on during breaks"
            : "Blocking pauses during breaks";

    public string SelectedPresetBonusBlocksLabel =>
        (SelectedPreset?.BonusBlocksFollowStrictness ?? false)
            ? "Bonus blocks follow the strictness"
            : "Bonus blocks are free";

    public ObservableCollection<RestDayChipViewModel> RestDayChips { get; } = new();
    public ObservableCollection<ContractPresetDto> SavedPresets { get; } = new();
    public bool HasNoSavedPresets => IsAdvanced && !IsLoadingPresets && SavedPresets.Count == 0;

    [ObservableProperty] private string? _error;

    public ContractRulesStepViewModel(
        bool isAdvanced,
        DateOnly startDate,
        DateOnly endDate,
        IQueryDispatcher queryDispatcher,
        Action onContinue,
        Action onBack,
        StrictnessRule? initialStrictnessRule = null,
        double? initialFixedDailyFloorHours = null,
        bool initialAllowCancellation = true,
        TimeOnly? initialWorkHoursStart = null,
        TimeOnly? initialWorkHoursEnd = null,
        IReadOnlyCollection<DateOnly>? initialRestDays = null,
        Guid? initialContractPresetId = null,
        RedemptionPlacementPolicy? initialRedemptionPlacementPolicy = null,
        bool initialKeepBlockingDuringBreaks = false,
        bool initialBonusBlocksFollowStrictness = false)
    {
        IsAdvanced = isAdvanced;
        StartDate = startDate;
        EndDate = endDate;
        _queryDispatcher = queryDispatcher;
        _onContinue = onContinue;
        _onBack = onBack;
        _initialStrictnessRule = initialStrictnessRule;
        _initialFixedDailyFloorHours = initialFixedDailyFloorHours;
        _initialAllowCancellation = initialAllowCancellation;
        _initialWorkHoursStart = initialWorkHoursStart;
        _initialWorkHoursEnd = initialWorkHoursEnd;
        _initialContractPresetId = initialContractPresetId;
        _initialRedemptionPlacementPolicy = initialRedemptionPlacementPolicy;
        _initialKeepBlockingDuringBreaks = initialKeepBlockingDuringBreaks;
        _initialBonusBlocksFollowStrictness = initialBonusBlocksFollowStrictness;

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
            _ = LoadPresetsAsync();
    }

    private async Task LoadPresetsAsync()
    {
        IsLoadingPresets = true;
        try
        {
            var presets = await _queryDispatcher.QueryAsync(new GetAllContractPresetsQuery());
            SavedPresets.Clear();
            foreach (var p in presets) SavedPresets.Add(p);

            if (_initialContractPresetId is { } presetId)
                SelectedPreset = SavedPresets.FirstOrDefault(p => p.Id == presetId);
        }
        finally
        {
            IsLoadingPresets = false;
            OnPropertyChanged(nameof(HasNoSavedPresets));
        }
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

        // No preset picked and nothing to fall back on (a brand-new contract, not an edit of
        // one that already had rules) — there's nothing to seal a contract with.
        if (SelectedPreset is null && _initialStrictnessRule is null)
        {
            Error = "Please choose a contract preset.";
            return;
        }

        _onContinue();
    }

    [RelayCommand] private void Back() => _onBack();

    // ── Resolved values, pulled by the shell after Continue succeeds ─────────
    // For Advanced, always prefer the picked preset's current values; fall back to whatever
    // this contract already had (editing an existing one) only when nothing is picked.

    public string ResolvedSummaryLabel => IsAdvanced
        ? $"Preset — {SelectedPreset?.Name ?? "Custom"}"
        : $"Simple — {SelectedSimpleOption?.Name}";

    public StrictnessRule ResolvedStrictnessRule => IsAdvanced
        ? SelectedPreset?.StrictnessRule ?? _initialStrictnessRule ?? StrictnessRule.DailyFlexibility
        : SelectedSimpleOption?.StrictnessRule ?? StrictnessRule.DailyFlexibility;

    public bool ResolvedAllowCancellation => IsAdvanced
        ? SelectedPreset?.AllowCancellation ?? _initialAllowCancellation
        : SelectedSimpleOption?.AllowCancellation ?? true;

    public double? ResolvedFixedDailyFloorHours => IsAdvanced
        ? SelectedPreset?.DailyFloorHours ?? _initialFixedDailyFloorHours
        : null;

    public IReadOnlyList<DateOnly> ResolvedRestDays => IsAdvanced
        ? RestDayChips.Where(c => c.IsRestDay).Select(c => c.Date).ToList()
        : [];

    public TimeOnly? ResolvedWorkHoursStart => IsAdvanced
        ? SelectedPreset?.WorkHoursStart ?? _initialWorkHoursStart
        : null;

    public TimeOnly? ResolvedWorkHoursEnd => IsAdvanced
        ? SelectedPreset?.WorkHoursEnd ?? _initialWorkHoursEnd
        : null;

    public Guid? ResolvedContractPresetId => IsAdvanced
        ? SelectedPreset?.Id ?? _initialContractPresetId
        : null;

    // Simple contracts always get the more permissive setting — no preset step exists to
    // pick anything else. Advanced always prefers the picked preset's current value, same
    // fallback-to-initial rule as every other Resolved* property above.
    public RedemptionPlacementPolicy ResolvedRedemptionPlacementPolicy => IsAdvanced
        ? SelectedPreset?.RedemptionPlacementPolicy ?? _initialRedemptionPlacementPolicy ?? RedemptionPlacementPolicy.AnywhereInContract
        : RedemptionPlacementPolicy.AnywhereInContract;

    // Same split (Gap 64): Simple keeps today's behavior (breaks lift blocking); Advanced
    // takes the picked preset's rule, falling back to the contract being edited.
    public bool ResolvedKeepBlockingDuringBreaks => IsAdvanced
        ? SelectedPreset?.KeepBlockingDuringBreaks ?? _initialKeepBlockingDuringBreaks
        : false;

    // Same split (Gap 93): Simple leaves Bonus blocks free; Advanced takes the picked preset's rule.
    public bool ResolvedBonusBlocksFollowStrictness => IsAdvanced
        ? SelectedPreset?.BonusBlocksFollowStrictness ?? _initialBonusBlocksFollowStrictness
        : false;
}
