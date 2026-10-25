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
using NenFocus.Domain.ValueObjects;

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
    // Null while the field holds text that isn't a time (the field shows it red).
    [ObservableProperty] private TimeSpan? _workHoursStart = TimeSpan.FromHours(9);
    [ObservableProperty] private TimeSpan? _workHoursEnd = TimeSpan.FromHours(17);
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
        WorkHoursStart = TimeSpan.FromHours(9);
        WorkHoursEnd = TimeSpan.FromHours(17);
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
        if (card.WorkHoursStart is { } start) WorkHoursStart = FieldTime(start);
        if (card.WorkHoursEnd is { } end) WorkHoursEnd = FieldTime(end);
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

        // The form's own input checks: what the text boxes can't turn into a command at all. The
        // settings' rules (1–24, fits the Work Hours) are ContractPreset.CheckSettings', asked by
        // the command (Gap 92, V9).
        int? floor = null;
        if (IsDailyFloorEnabled)
        {
            if (!int.TryParse(DailyFloorHoursText, out var parsedFloor))
            {
                FormError = "Daily floor must be a whole number of hours.";
                return;
            }
            floor = parsedFloor;
        }

        WorkHours? workHours = null;
        if (IsWorkHoursEnabled)
        {
            if (WorkHoursStart is not { } startField || WorkHoursEnd is not { } endField)
            {
                FormError = "Work hours must be valid times.";
                return;
            }
            workHours = WorkHours.TryCreate(WorkHoursTime(startField), WorkHoursTime(endField));
            if (workHours is null)
            {
                FormError = "Work hours end must be after start.";
                return;
            }
        }

        if (IsEditing && _editingPresetId.HasValue)
        {
            var result = await _commandDispatcher.DispatchAsync(new UpdateContractPresetCommand
            {
                PresetId = _editingPresetId.Value,
                Name = name,
                StrictnessRule = FormStrictnessRule,
                DailyFloorHours = floor,
                WorkHours = workHours,
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
                WorkHours = workHours,
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

    // WorkHours keeps the end of the day as TimeOnly.MaxValue, a field as 24:00 — mapped here both
    // ways, so a preset ending at midnight reopens as 24:00 and saves unchanged (it used to come back
    // as 23:59). A WorkHours quirk, not a text one → Gap 82.
    private static TimeOnly WorkHoursTime(TimeSpan field) =>
        field >= TimeSpan.FromDays(1) ? TimeOnly.MaxValue : TimeOnly.FromTimeSpan(field);

    private static TimeSpan FieldTime(TimeOnly time) =>
        time == TimeOnly.MaxValue ? TimeSpan.FromDays(1) : time.ToTimeSpan();
}
