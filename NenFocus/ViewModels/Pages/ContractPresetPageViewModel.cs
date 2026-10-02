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
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;
using NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;
using NenFocus.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Pages;

/// <summary>
/// One card's worth of already-resolved display text for a ContractPresetDto — kept a plain
/// wrapper (no math, no formatting logic of its own) since the labels it exposes are built
/// once from the DTO's already-typed fields, mirroring BlockPresetCardViewModel's role.
/// </summary>
public partial class ContractPresetCardViewModel : ViewModelBase
{
    public Guid Id { get; }
    public string Name { get; }
    public StrictnessRule StrictnessRule { get; }
    public double? DailyFloorHours { get; }
    public TimeOnly? WorkHoursStart { get; }
    public TimeOnly? WorkHoursEnd { get; }
    public bool AllowCancellation { get; }
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; }
    public bool KeepBlockingDuringBreaks { get; }
    public bool BonusBlocksFollowStrictness { get; }

    public string FloorLabel => DailyFloorToLabelConverter.FormatFloor(DailyFloorHours);
    public string WorkHoursLabel => WorkHoursRangeToLabelConverter.FormatRange(WorkHoursStart, WorkHoursEnd);
    public string CancellationLabel => AllowCancellation ? "Cancellation allowed" : "No cancellation";
    public string RedemptionPlacementLabel => RedemptionPlacementPolicy == RedemptionPlacementPolicy.CurrentDayOnly
        ? "Redemption: current day only"
        : "Redemption: anywhere in the contract";
    public string BreakBlockingLabel => KeepBlockingDuringBreaks
        ? "Blocking stays on during breaks"
        : "Blocking pauses during breaks";
    public string BonusBlocksLabel => BonusBlocksFollowStrictness
        ? "Bonus blocks follow the strictness"
        : "Bonus blocks are free";

    public ContractPresetCardViewModel(ContractPresetDto dto)
    {
        Id = dto.Id;
        Name = dto.Name;
        StrictnessRule = dto.StrictnessRule;
        DailyFloorHours = dto.DailyFloorHours;
        WorkHoursStart = dto.WorkHoursStart;
        WorkHoursEnd = dto.WorkHoursEnd;
        AllowCancellation = dto.AllowCancellation;
        RedemptionPlacementPolicy = dto.RedemptionPlacementPolicy;
        KeepBlockingDuringBreaks = dto.KeepBlockingDuringBreaks;
        BonusBlocksFollowStrictness = dto.BonusBlocksFollowStrictness;
    }
}

public partial class ContractPresetPageViewModel : ViewModelBase
{
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ICommandDispatcher _commandDispatcher;

    private Guid? _editingPresetId;

    public ObservableCollection<ContractPresetCardViewModel> Presets { get; } = new();
    [ObservableProperty] private bool _isLoading;
    public bool HasNoPresets => Presets.Count == 0 && !IsLoading;

    public IReadOnlyList<StrictnessRule> StrictnessOptions { get; } = Enum.GetValues<StrictnessRule>();
    public IReadOnlyList<RedemptionPlacementPolicy> RedemptionPlacementOptions { get; } = Enum.GetValues<RedemptionPlacementPolicy>();
    public ObservableCollection<string> TimeOptions { get; } = new();

    // ── Form state ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Contract Preset";
    [ObservableProperty] private string? _formError;

    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private StrictnessRule _formStrictnessRule = StrictnessRule.DailyFlexibility;
    [ObservableProperty] private bool _isDailyFloorEnabled;
    [ObservableProperty] private string _dailyFloorHoursText = "3";
    [ObservableProperty] private bool _allowCancellation = true;
    [ObservableProperty] private bool _keepBlockingDuringBreaks;
    [ObservableProperty] private bool _bonusBlocksFollowStrictness;
    [ObservableProperty] private bool _isWorkHoursEnabled;
    [ObservableProperty] private string _workHoursStartText = "09:00";
    [ObservableProperty] private string _workHoursEndText = "17:00";
    [ObservableProperty] private RedemptionPlacementPolicy _formRedemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Preset";
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(SaveButtonLabel));

    [ObservableProperty] private bool _isConfirmDeleteVisible;
    [ObservableProperty] private string _confirmDeleteMessage = string.Empty;
    [ObservableProperty] private string? _confirmDeleteError;
    private ContractPresetCardViewModel? _pendingDeleteCard;

    public ContractPresetPageViewModel(IQueryDispatcher queryDispatcher, ICommandDispatcher commandDispatcher)
    {
        _queryDispatcher = queryDispatcher;
        _commandDispatcher = commandDispatcher;

        for (int h = 0; h < 24; h++)
            for (int m = 0; m < 60; m += 15)
                TimeOptions.Add($"{h:D2}:{m:D2}");
        TimeOptions.Add("24:00");

        Presets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoPresets));
        _ = LoadPresetsAsync();
    }

    private async Task LoadPresetsAsync()
    {
        IsLoading = true;
        var presets = await _queryDispatcher.QueryAsync(new GetAllContractPresetsQuery());
        Presets.Clear();
        foreach (var p in presets)
            Presets.Add(new ContractPresetCardViewModel(p));
        IsLoading = false;
    }

    private void ResetForm()
    {
        FormName = string.Empty;
        FormStrictnessRule = StrictnessRule.DailyFlexibility;
        IsDailyFloorEnabled = false;
        DailyFloorHoursText = "3";
        AllowCancellation = true;
        IsWorkHoursEnabled = false;
        WorkHoursStartText = "09:00";
        WorkHoursEndText = "17:00";
        FormRedemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract;
        KeepBlockingDuringBreaks = false;
        BonusBlocksFollowStrictness = false;
        FormError = null;
    }

    [RelayCommand]
    private void NewPreset()
    {
        _editingPresetId = null;
        IsEditing = false;
        FormTitle = "New Contract Preset";
        ResetForm();
        IsFormVisible = true;
    }

    [RelayCommand]
    private void EditPreset(ContractPresetCardViewModel card)
    {
        _editingPresetId = card.Id;
        IsEditing = true;
        FormTitle = "Edit Contract Preset";
        ResetForm();
        FormName = card.Name;
        FormStrictnessRule = card.StrictnessRule;
        IsDailyFloorEnabled = card.DailyFloorHours.HasValue;
        if (card.DailyFloorHours.HasValue) DailyFloorHoursText = card.DailyFloorHours.Value.ToString("0.##");
        AllowCancellation = card.AllowCancellation;
        IsWorkHoursEnabled = card.WorkHoursStart.HasValue;
        if (card.WorkHoursStart.HasValue) WorkHoursStartText = FormatTime(card.WorkHoursStart.Value);
        if (card.WorkHoursEnd.HasValue) WorkHoursEndText = FormatTime(card.WorkHoursEnd.Value);
        FormRedemptionPlacementPolicy = card.RedemptionPlacementPolicy;
        KeepBlockingDuringBreaks = card.KeepBlockingDuringBreaks;
        BonusBlocksFollowStrictness = card.BonusBlocksFollowStrictness;
        IsFormVisible = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        var name = FormName.Trim();
        if (string.IsNullOrEmpty(name)) { FormError = "Preset name is required."; return; }

        double? floor = null;
        if (IsDailyFloorEnabled)
        {
            if (!double.TryParse(DailyFloorHoursText, out var parsedFloor))
            {
                FormError = "Daily floor must be a number.";
                return;
            }
            floor = parsedFloor;
        }

        TimeOnly? workHoursStart = null, workHoursEnd = null;
        if (IsWorkHoursEnabled)
        {
            if (!TryParseTime(WorkHoursStartText, out var start) || !TryParseTime(WorkHoursEndText, out var end))
            {
                FormError = "Work hours must be valid times.";
                return;
            }
            workHoursStart = start;
            workHoursEnd = end;
        }

        if (IsEditing && _editingPresetId.HasValue)
        {
            var result = await _commandDispatcher.DispatchAsync(new UpdateContractPresetCommand
            {
                PresetId = _editingPresetId.Value,
                Name = name,
                StrictnessRule = FormStrictnessRule,
                DailyFloorHours = floor,
                WorkHoursStart = workHoursStart,
                WorkHoursEnd = workHoursEnd,
                AllowCancellation = AllowCancellation,
                RedemptionPlacementPolicy = FormRedemptionPlacementPolicy,
                KeepBlockingDuringBreaks = KeepBlockingDuringBreaks,
                BonusBlocksFollowStrictness = BonusBlocksFollowStrictness
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            var result = await _commandDispatcher.DispatchAsync(new CreateContractPresetCommand
            {
                Name = name,
                StrictnessRule = FormStrictnessRule,
                DailyFloorHours = floor,
                WorkHoursStart = workHoursStart,
                WorkHoursEnd = workHoursEnd,
                AllowCancellation = AllowCancellation,
                RedemptionPlacementPolicy = FormRedemptionPlacementPolicy,
                KeepBlockingDuringBreaks = KeepBlockingDuringBreaks,
                BonusBlocksFollowStrictness = BonusBlocksFollowStrictness
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }

        await LoadPresetsAsync();
        CloseForm();
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        _editingPresetId = null;
    }

    [RelayCommand]
    private void RequestDeletePreset(ContractPresetCardViewModel card)
    {
        _pendingDeleteCard = card;
        ConfirmDeleteMessage = $"Delete \"{card.Name}\"? This cannot be undone.";
        ConfirmDeleteError = null;
        IsConfirmDeleteVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_pendingDeleteCard is null) return;
        var result = await _commandDispatcher.DispatchAsync(
            new DeleteContractPresetCommand { PresetId = _pendingDeleteCard.Id });
        if (!result.Success)
        {
            ConfirmDeleteError = result.ErrorMessage;
            return;
        }
        Presets.Remove(_pendingDeleteCard);
        _pendingDeleteCard = null;
        IsConfirmDeleteVisible = false;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDeleteCard = null;
        ConfirmDeleteError = null;
        IsConfirmDeleteVisible = false;
    }

    private static bool TryParseTime(string s, out TimeOnly time)
    {
        if (s == "24:00") { time = TimeOnly.MaxValue; return true; }
        return TimeOnly.TryParse(s, out time);
    }

    private static string FormatTime(TimeOnly t) => t.ToString("HH:mm");
}
