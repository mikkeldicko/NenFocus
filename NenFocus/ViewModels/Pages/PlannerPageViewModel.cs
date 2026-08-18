using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetActiveNenContract;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.Queries.GetScheduledNenContracts;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Components;
using NenFocus.ViewModels.Contracts;

namespace NenFocus.ViewModels.Pages;

public partial class PlannerPageViewModel : ViewModelBase, IDisposable
{
    public const double HourRowHeight = 56.0;
    public const int FirstHour = 0;
    public const int LastHour = 24;
    public const int TotalHours = LastHour - FirstHour;
    public double GridContentHeight => HourRowHeight * TotalHours;

    private readonly IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> _getPresets;
    private readonly IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> _getBlocksForDay;
    private readonly ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> _createBlock;
    private readonly ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> _updateBlock;
    private readonly ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> _deleteBlock;
    private readonly ICommandHandler<SealNenContractCommand, SealNenContractResult> _sealContract;
    private readonly ICommandHandler<BreakNenContractCommand, BreakNenContractResult> _breakContract;
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _computePlannedFocusHours;
    private readonly IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> _getContractPresets;
    private readonly ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> _createContractPreset;
    private readonly IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?> _getActiveContract;
    private readonly IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> _checkOverlap;
    private readonly IQueryHandler<GetScheduledNenContractsQuery, IReadOnlyList<ScheduledNenContractDto>> _getScheduledContracts;
    private readonly IQueryHandler<GetNenContractForEditQuery, NenContractEditDto?> _getContractForEdit;
    private readonly ICommandHandler<EditNenContractCommand, EditNenContractResult> _editContract;
    private readonly ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult> _deleteContract;
    private readonly IPlannerClockService _clockService;
    private readonly ISessionStateService _sessionState;

    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();
    private readonly List<BlockPresetDto> _allPresets = new();
    private readonly List<ContractDraftBlock> _draftBlocks = new();
    private Guid? _editingBlockId;
    private bool _editingIsDraft;
    private NenContractDraftRules? _draftRules;

    // Set only while drafting is seeded from an existing Scheduled contract (Edit flow).
    // Carries the original blocks (converted into draft tiles by BeginDrafting) so nothing
    // needs to be re-fetched at Seal time; cleared by ExitDraftingMode.
    private NenContractEditDto? _editingContractPayload;

    [ObservableProperty] private DateTime _weekStart;

    /// <summary>
    /// Returns the Grid.Column index for the red "Current Time" line.
    /// Column 0 is hour labels, so Column 1 is Monday, Column 7 is Sunday.
    /// </summary>
    public int TodayColumnIndex
    {
        get
        {
            int day = (int)_clockService.LocalNow.DayOfWeek;
            int mondayBasedIndex = (day == 0) ? 6 : day - 1;
            return mondayBasedIndex + 1;
        }
    }

    public ObservableCollection<WeekDayHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<HourLabelViewModel> HourLabels { get; } = new();
    public ObservableCollection<PlannedBlockTileViewModel> Tiles { get; } = new();
    public ObservableCollection<BlockPresetDto> AvailablePresets { get; } = new();
    public ObservableCollection<string> TimeOptions { get; } = new();
    // Per-day floor badge lookup, keyed by date — feeds the day-header badges. Populated
    // either by RefreshDayFloorPreviewsAsync (drafting) or RefreshActiveContractAsync
    // (sealed/active contract); the two are mutually exclusive for any viewed week, since
    // an in-effect contract can never overlap the date range being drafted. Not an
    // ObservableCollection — the whole dictionary is replaced on refresh so day-header
    // bindings (which look up by key) see one atomic update instead of N incremental ones.
    [ObservableProperty] private Dictionary<DateOnly, DayFloorPreviewViewModel> _dayFloorByDate = new();

    // "X of Y met" / "All daily floors met", excluding rest days — shown in the drafting
    // action bar. Purely a rephrasing of DayFloorByDate's own IsMet/IsRestDay flags.
    public string DraftFloorSummaryText
    {
        get
        {
            var relevant = DayFloorByDate.Values.Where(d => !d.IsRestDay).ToList();
            if (relevant.Count == 0) return string.Empty;
            var met = relevant.Count(d => d.IsMet);
            return met == relevant.Count ? "All daily floors met" : $"{met} of {relevant.Count} met";
        }
    }

    partial void OnDayFloorByDateChanged(Dictionary<DateOnly, DayFloorPreviewViewModel> value)
    {
        OnPropertyChanged(nameof(DraftFloorSummaryText));
        foreach (var header in DayHeaders)
            header.DayFloorStatus = value.GetValueOrDefault(DateOnly.FromDateTime(header.Date));
    }

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Block";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private DateTimeOffset _formDate;

    [ObservableProperty] private double _currentTimeY;
    [ObservableProperty] private bool _isTodayInView;

    [ObservableProperty] private string _formStartTime = "09:00";
    [ObservableProperty] private string _formEndTime = "10:00";

    [ObservableProperty] private bool _isFocusBlock = true;
    [ObservableProperty] private BlockPresetDto? _selectedPreset;
    [ObservableProperty] private bool _isStartLocked;
    [ObservableProperty] private bool _isEditingCompleted;
    [ObservableProperty] private bool _isEditingBlockEnded;
    [ObservableProperty] private bool _isEditingBlockContractLocked;
    [ObservableProperty] private string? _dragError;

    // ── Nen Contract wizard + drafting mode ───────────────────────────────────
    [ObservableProperty] private bool _isWizardVisible;
    [ObservableProperty] private ContractWizardViewModel? _wizardViewModel;
    [ObservableProperty] private bool _isDraftingContract;
    [ObservableProperty] private string? _draftContractName;
    [ObservableProperty] private bool _isSealing;
    [ObservableProperty] private string? _sealErrorMessage;
    [ObservableProperty] private bool _isConfirmDiscardDraftVisible;
    [ObservableProperty] private string _confirmDiscardDraftMessage = string.Empty;

    // ── Active-contract banner (persists for the contract's full duration, independent
    // of drafting mode) ─────────────────────────────────────────────────────────────
    [ObservableProperty] private ActiveNenContractDto? _activeContract;
    [ObservableProperty] private bool _isConfirmCancelContractVisible;
    [ObservableProperty] private string _confirmCancelContractMessage = string.Empty;
    [ObservableProperty] private string? _cancelContractErrorMessage;

    // ── Nen Contract popover (replaces the old top-right "Make Nen Contract" button) ──
    [ObservableProperty] private bool _isNenContractPopoverVisible;
    public ObservableCollection<ScheduledNenContractDto> ScheduledContracts { get; } = new();
    public bool HasNoContracts => ActiveContract is null && ScheduledContracts.Count == 0;

    [ObservableProperty] private bool _isConfirmDeleteContractVisible;
    [ObservableProperty] private string _confirmDeleteContractMessage = string.Empty;
    [ObservableProperty] private string? _deleteContractErrorMessage;
    private Guid? _contractIdPendingDelete;

    public bool HasActiveContract => ActiveContract is not null;

    // AllowCancellation is deliberately not enforced here (interim, 2026-08-18) — mirrors
    // NenContract.Break's own comment; every active contract can be cancelled until Strict
    // Mode exists as the real gate. Only presence of an active contract matters now.
    public bool CanCancelActiveContract => HasActiveContract;

    public string ActiveContractBannerText => ActiveContract is null
        ? string.Empty
        : $"Contract: {ActiveContract.Name} — Day {ActiveContract.DayNumber}/{ActiveContract.TotalDays} — " +
          $"{ActiveContract.DaysBehind} of {ActiveContract.DaysConsidered} days behind — {ActiveContract.DeficitHours:0.#}h deficit — " +
          $"{ActiveContract.DaysConsidered - ActiveContract.DaysBehind} of {ActiveContract.DaysConsidered} days met";

    // 3-second live poll for the day-header floor badges while an active contract is in
    // view — so today's badge reflects an in-progress session's elapsed hours without
    // waiting for the next week-nav/seal/session-end refresh. No page-visibility gating:
    // this ViewModel already lives for the app's whole lifetime (not recreated on
    // navigation, and there's no such hook anywhere in this codebase), and the cost is
    // comparable to ScheduledEnforcementService's own always-on 5s heartbeat. Started/
    // stopped by OnActiveContractChanged, not by a page-visible/hidden signal.
    private readonly System.Timers.Timer _floorStatusPollTimer = new(3000) { AutoReset = true };

    partial void OnActiveContractChanged(ActiveNenContractDto? value)
    {
        OnPropertyChanged(nameof(HasActiveContract));
        OnPropertyChanged(nameof(ActiveContractBannerText));
        OnPropertyChanged(nameof(CanCancelActiveContract));
        OnPropertyChanged(nameof(HasNoContracts));

        if (value is not null) _floorStatusPollTimer.Start();
        else _floorStatusPollTimer.Stop();
    }

    // Scoped to the currently viewed week — the banner only shows when a contract's date
    // range overlaps the week actually on screen, not just "some contract is in effect
    // somewhere." Must be re-run on every week navigation, not only on init/seal/session-end.
    private async Task RefreshActiveContractAsync()
    {
        var weekStartDate = DateOnly.FromDateTime(WeekStart);
        var weekEndDate = weekStartDate.AddDays(6);
        ActiveContract = await _getActiveContract.HandleAsync(new GetActiveNenContractQuery(weekStartDate, weekEndDate));

        // Drafting owns DayFloorByDate while it's active (RefreshDayFloorPreviewsAsync) —
        // an in-effect contract can never overlap the date range being drafted, so this
        // never runs concurrently with a real draft, but skip anyway rather than relying
        // on that invariant holding forever.
        if (IsDraftingContract) return;

        DayFloorByDate = ActiveContract?.DayStatuses.ToDictionary(
            d => d.Date,
            d => new DayFloorPreviewViewModel(d.Date, d.CurrentHours, d.IsRestDay ? null : d.FloorHours, d.IsRestDay, d.Timing))
            ?? new Dictionary<DateOnly, DayFloorPreviewViewModel>();
    }

    [RelayCommand]
    private void RequestCancelContract()
    {
        if (ActiveContract is null) return;
        CancelContractErrorMessage = null;
        ConfirmCancelContractMessage = "Are you sure you want to cancel this contract? This cannot be undone.";
        IsConfirmCancelContractVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCancelContractAsync()
    {
        if (ActiveContract is null) { IsConfirmCancelContractVisible = false; return; }

        var result = await _breakContract.HandleAsync(new BreakNenContractCommand { ContractId = ActiveContract.Id });
        IsConfirmCancelContractVisible = false;

        if (!result.Success)
        {
            CancelContractErrorMessage = result.ErrorMessage;
            return;
        }

        IsNenContractPopoverVisible = false;
        await RefreshActiveContractAsync();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private void CancelCancelContract() => IsConfirmCancelContractVisible = false;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Block";

    // Mirrors DeletePlannedBlockValidator's rejection conditions exactly — sourced from
    // dto.HasEnded / dto.IsUnderInEffectContract (Application-computed, same predicates the
    // validator calls) and the live active-session noticeboard, not re-derived here.
    public bool CanDeleteFromForm =>
        IsEditing && !_isEditingBlockEnded && !_isEditingBlockContractLocked
        && _sessionState.ActiveSession?.OriginatingBlockId != _editingBlockId;

    // True when the editing block is either currently running (active session) or already completed.
    // Controls visibility of the Save button and disables form inputs.
    public bool IsBlockLocked =>
        IsEditing && (_isEditingCompleted || _sessionState.ActiveSession?.OriginatingBlockId == _editingBlockId);

    public bool CanSaveBlock => !IsBlockLocked;

    // Deliberately NOT gated on session-active — confirmed with the architect: the only
    // reason to disable Make/Edit is a genuine overlay collision (a form or the wizard
    // already open), not merely a session running. This differs from the old top-right
    // button's original guard, which this popover fully replaces.
    public bool CanOpenContractWizard =>
        !IsFormVisible && !IsWizardVisible && !IsDraftingContract;

    // Shell renders the wizard as a full-screen overlay (mirrors the Ritual overlay), so it
    // needs to know when one opens/closes even though this ViewModel still owns construction
    // and the drafting handoff — same event-bridge shape as ISessionStateService's
    // RitualRequirementChanged, which ShellViewModel already consumes.
    public event Action<ContractWizardViewModel>? ContractWizardOpened;
    public event Action? ContractWizardClosed;

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(CanDeleteFromForm));
        OnPropertyChanged(nameof(IsBlockLocked));
        OnPropertyChanged(nameof(CanSaveBlock));
    }

    partial void OnIsEditingCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBlockLocked));
        OnPropertyChanged(nameof(CanSaveBlock));
    }

    partial void OnIsEditingBlockEndedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeleteFromForm));
    }

    partial void OnIsEditingBlockContractLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeleteFromForm));
    }

    partial void OnIsFormVisibleChanged(bool value) => OnPropertyChanged(nameof(CanOpenContractWizard));
    partial void OnIsWizardVisibleChanged(bool value) => OnPropertyChanged(nameof(CanOpenContractWizard));
    partial void OnIsDraftingContractChanged(bool value) => OnPropertyChanged(nameof(CanOpenContractWizard));

    public PlannerPageViewModel(
        IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> getPresets,
        IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
        ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createBlock,
        ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updateBlock,
        ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deleteBlock,
        ICommandHandler<SealNenContractCommand, SealNenContractResult> sealContract,
        ICommandHandler<BreakNenContractCommand, BreakNenContractResult> breakContract,
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours,
        IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> getContractPresets,
        ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> createContractPreset,
        IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?> getActiveContract,
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap,
        IQueryHandler<GetScheduledNenContractsQuery, IReadOnlyList<ScheduledNenContractDto>> getScheduledContracts,
        IQueryHandler<GetNenContractForEditQuery, NenContractEditDto?> getContractForEdit,
        ICommandHandler<EditNenContractCommand, EditNenContractResult> editContract,
        ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult> deleteContract,
        IPlannerClockService clockService,
        ISessionStateService sessionState)
    {
        _getPresets = getPresets;
        _getBlocksForDay = getBlocksForDay;
        _createBlock = createBlock;
        _updateBlock = updateBlock;
        _deleteBlock = deleteBlock;
        _sealContract = sealContract;
        _getScheduledContracts = getScheduledContracts;
        _getContractForEdit = getContractForEdit;
        _editContract = editContract;
        _deleteContract = deleteContract;
        _breakContract = breakContract;
        _computePlannedFocusHours = computePlannedFocusHours;
        _getContractPresets = getContractPresets;
        _createContractPreset = createContractPreset;
        _getActiveContract = getActiveContract;
        _checkOverlap = checkOverlap;
        _clockService = clockService;
        _sessionState = sessionState;
        _sessionState.SessionChanged += OnSessionChanged;

        for (int h = FirstHour; h < LastHour; h++)
            HourLabels.Add(new HourLabelViewModel(h));

        GenerateTimeOptions();

        WeekStart = StartOfWeek(_clockService.LocalNow.Date);
        BuildDayHeaders();

        _clockService.Tick += OnClockTick;
        UpdateCurrentTimePosition();

        _floorStatusPollTimer.Elapsed += OnFloorStatusPollTick;

        _ = InitAsync();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateCurrentTimePosition);
    }

    private void OnFloorStatusPollTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => _ = RefreshActiveContractAsync());
    }

    private void OnSessionChanged(object? sender, SessionDto? session)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsBlockLocked));
            OnPropertyChanged(nameof(CanSaveBlock));
            OnPropertyChanged(nameof(CanDeleteFromForm));
            OnPropertyChanged(nameof(CanOpenContractWizard));
            if (session is null)
            {
                _ = LoadWeekAsync();
                _ = RefreshActiveContractAsync(); // a finished session may move Day Met/deficit
            }
        });
    }

    private void UpdateCurrentTimePosition()
    {
        var now = _clockService.LocalNow;
        var weekEnd = WeekStart.AddDays(7);
        IsTodayInView = now.Date >= WeekStart.Date && now.Date < weekEnd.Date;

        if (IsTodayInView)
            CurrentTimeY = PlannerLayout.GetCurrentTimeY(now, FirstHour, HourRowHeight);

        OnPropertyChanged(nameof(TodayColumnIndex));
    }

    private void GenerateTimeOptions()
    {
        TimeOptions.Clear();
        for (int h = 0; h < 24; h++)
        {
            for (int m = 0; m < 60; m += 15)
            {
                TimeOptions.Add($"{h:D2}:{m:D2}");
            }
        }
        TimeOptions.Add("24:00");
    }

    private async Task InitAsync()
    {
        await LoadPresetsAsync();
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    [RelayCommand]
    private async Task PreviousWeekAsync() => await NavigateWeekAsync(WeekStart.AddDays(-7));

    [RelayCommand]
    private async Task NextWeekAsync() => await NavigateWeekAsync(WeekStart.AddDays(7));

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        var today = StartOfWeek(_clockService.LocalNow.Date);
        if (today == WeekStart) return;
        await NavigateWeekAsync(today);
    }

    // While drafting a contract, week navigation is clamped to weeks that overlap the
    // contract's date range — dragging blocks outside the range makes no sense mid-draft.
    private async Task NavigateWeekAsync(DateTime candidateWeekStart)
    {
        if (IsDraftingContract && !WeekOverlapsDraftRange(candidateWeekStart)) return;

        WeekStart = candidateWeekStart;
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    private bool WeekOverlapsDraftRange(DateTime candidateWeekStart)
    {
        if (_draftRules is null) return true;
        var weekEnd = candidateWeekStart.AddDays(6);
        var rangeStart = _draftRules.StartDate.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = _draftRules.EndDate.ToDateTime(TimeOnly.MinValue);
        return weekEnd.Date >= rangeStart.Date && candidateWeekStart.Date <= rangeEnd.Date;
    }

    private void BuildDayHeaders()
    {
        var today = _clockService.LocalNow.Date;
        DayHeaders.Clear();
        for (int i = 0; i < 7; i++)
            DayHeaders.Add(new WeekDayHeaderViewModel(WeekStart.AddDays(i), today));
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }

    private async Task LoadPresetsAsync()
    {
        var presets = await _getPresets.HandleAsync(new GetAllBlockPresetsQuery());
        _allPresets.Clear();
        _allPresets.AddRange(presets);
        RefreshAvailablePresets();
    }

    private void RefreshAvailablePresets()
    {
        var requiredType = IsFocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;
        AvailablePresets.Clear();
        foreach (var p in _allPresets.Where(p => p.PresetType == requiredType))
            AvailablePresets.Add(p);
    }

    partial void OnIsFocusBlockChanged(bool value)
    {
        RefreshAvailablePresets();
        SelectedPreset = AvailablePresets.FirstOrDefault();
    }

    private async Task LoadWeekAsync()
    {
        IsLoading = true;
        Tiles.Clear();
        _blocksInWeek.Clear();

        for (int i = 0; i < 7; i++)
        {
            var day = WeekStart.AddDays(i);
            var query = new GetPlannedBlocksForDayQuery { Date = DateOnly.FromDateTime(day) };
            var blocks = await _getBlocksForDay.HandleAsync(query);

            foreach (var b in blocks)
            {
                _blocksInWeek[b.Id] = b;
                var tile = BuildTile(b, i);
                if (tile is not null) Tiles.Add(tile);
            }
        }

        if (IsDraftingContract)
        {
            AppendDraftTiles();
            await RefreshDayFloorPreviewsAsync();
        }

        IsLoading = false;
    }

    private PlannedBlockTileViewModel? BuildTile(PlannedBlockDto dto, int columnIndex)
    {
        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();

        var dayStart = WeekStart.AddDays(columnIndex);
        var visibleStart = dayStart.AddHours(FirstHour);
        var visibleEnd = dayStart.AddHours(LastHour);

        var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
        if (position is null) return null;

        var localDto = dto with { Start = localStart, End = localEnd };
        return new PlannedBlockTileViewModel(localDto, dto.BlockPresetType, columnIndex, position.Value.Top, position.Value.Height);
    }

    [RelayCommand]
    private async Task NewBlockAtAsync(GridSlotPosition slot)
    {
        await EnsurePresetsLoadedAsync();
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        IsStartLocked = false;
        FormTitle = "New Block";
        ResetForm();

        var blockDate = DateTime.SpecifyKind(WeekStart.AddDays(slot.ColumnIndex).Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        FormStartTime = $"{slot.Hour:D2}:00";
        FormEndTime = $"{(slot.Hour + 1):D2}:00";
        SelectedPreset = AvailablePresets.FirstOrDefault();
        IsFormVisible = true;
    }

    [RelayCommand]
    private async Task EditBlockAsync(PlannedBlockTileViewModel tile)
    {
        if (tile.IsDraft)
        {
            var draft = _draftBlocks.FirstOrDefault(d => d.Id == tile.Id);
            if (draft is null) return;
            await EnsurePresetsLoadedAsync();
            _editingBlockId = draft.Id;
            _editingIsDraft = true;
            IsEditing = true;
            IsStartLocked = false;
            IsEditingCompleted = false;
            IsEditingBlockEnded = false;
            IsEditingBlockContractLocked = false;
            FormTitle = "Edit Draft Block";
            ResetForm();

            var draftLocalStart = draft.Start.ToLocalTime();
            var draftLocalEnd = draft.End.ToLocalTime();
            var draftBlockDate = DateTime.SpecifyKind(draftLocalStart.Date, DateTimeKind.Utc);
            FormDate = new DateTimeOffset(draftBlockDate, TimeSpan.Zero);

            FormStartTime = $"{draftLocalStart.Hour:D2}:{draftLocalStart.Minute:D2}";
            FormEndTime = $"{draftLocalEnd.Hour:D2}:{draftLocalEnd.Minute:D2}";
            IsFocusBlock = draft.IsFocus;
            SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Id == draft.PresetId)
                              ?? AvailablePresets.FirstOrDefault();
            IsFormVisible = true;
            return;
        }

        if (_sessionState.ActiveSession?.OriginatingBlockId == tile.Id) return;
        if (!_blocksInWeek.TryGetValue(tile.Id, out var dto)) return;
        await EnsurePresetsLoadedAsync();
        _editingBlockId = dto.Id;
        _editingIsDraft = false;
        IsEditing = true;
        IsStartLocked = dto.Start <= DateTime.UtcNow;
        IsEditingCompleted = dto.IsCompleted;
        IsEditingBlockEnded = dto.HasEnded;
        IsEditingBlockContractLocked = dto.IsUnderInEffectContract;
        FormTitle = "Edit Block";
        ResetForm();

        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();
        var blockDate = DateTime.SpecifyKind(localStart.Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        FormStartTime = $"{localStart.Hour:D2}:{localStart.Minute:D2}";
        FormEndTime = $"{localEnd.Hour:D2}:{localEnd.Minute:D2}";
        IsFocusBlock = dto.BlockPresetType == BlockPresetType.Focus;
        SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Id == dto.PresetId)
                          ?? AvailablePresets.FirstOrDefault();
        IsFormVisible = true;
    }

    private async Task EnsurePresetsLoadedAsync()
    {
        if (AvailablePresets.Count == 0) await LoadPresetsAsync();
    }

    private void ResetForm()
    {
        FormError = null;
        IsFocusBlock = true;
        SelectedPreset = null;
    }

    [RelayCommand] private void SetFocusBlock() => IsFocusBlock = true;
    [RelayCommand] private void SetBackgroundBlock() => IsFocusBlock = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsDraftingContract)
        {
            if (!SaveDraftBlock()) return;
            await RefreshDayFloorPreviewsAsync();
            CloseForm();
            return;
        }

        FormError = null;
        if (SelectedPreset is null) { FormError = "Please select a block preset."; return; }
        if (!TryParseFormTime(FormStartTime, out var startTime) || !TryParseFormTime(FormEndTime, out var endTime))
        {
            FormError = "Invalid time format. Use HH:mm.";
            return;
        }
        if (endTime <= startTime) { FormError = "End time must be after start time."; return; }

        var dayDate = FormDate.Date;
        var localStart = DateTime.SpecifyKind(dayDate.Add(startTime), DateTimeKind.Local);
        var localEnd = DateTime.SpecifyKind(dayDate.Add(endTime), DateTimeKind.Local);
        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        var blockPresetType = IsFocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;

        if (IsEditing && _editingBlockId.HasValue)
        {
            var typeChanged = _blocksInWeek.TryGetValue(_editingBlockId.Value, out var originalDto)
                && originalDto.BlockPresetType != blockPresetType;

            if (typeChanged)
            {
                var deleteResult = await _deleteBlock.HandleAsync(
                    new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
                if (!deleteResult.Success) { FormError = deleteResult.ErrorMessage; return; }

                var createCmd = new CreatePlannedBlockCommand
                {
                    PresetId = SelectedPreset.Id,
                    BlockPresetType = blockPresetType,
                    Start = startUtc,
                    End = endUtc,
                    ContractId = originalDto!.ContractId
                };
                var createResult = await _createBlock.HandleAsync(createCmd);
                if (!createResult.Success) { FormError = createResult.ErrorMessage; return; }
            }
            else
            {
                var result = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
                {
                    BlockId = _editingBlockId.Value,
                    PresetId = SelectedPreset.Id,
                    Start = startUtc,
                    End = endUtc
                });
                if (!result.Success) { FormError = result.ErrorMessage; return; }
            }
        }
        else
        {
            var result = await _createBlock.HandleAsync(new CreatePlannedBlockCommand
            {
                PresetId = SelectedPreset.Id,
                BlockPresetType = blockPresetType,
                Start = startUtc,
                End = endUtc
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        await LoadWeekAsync();
        CloseForm();
    }

    // Validates and commits a draft block into the in-memory _draftBlocks list — no
    // repository call, nothing persists until the contract is sealed. Returns false (and
    // leaves FormError set) on a rejected placement — callers must not close the form or
    // clear FormError in that case, or the rejection message is wiped before it's seen.
    private bool SaveDraftBlock()
    {
        FormError = null;
        if (SelectedPreset is null) { FormError = "Please select a block preset."; return false; }
        if (!TryParseFormTime(FormStartTime, out var startTime) || !TryParseFormTime(FormEndTime, out var endTime))
        {
            FormError = "Invalid time format. Use HH:mm.";
            return false;
        }
        if (endTime <= startTime) { FormError = "End time must be after start time."; return false; }

        var dayDate = FormDate.Date;
        var localStart = DateTime.SpecifyKind(dayDate.Add(startTime), DateTimeKind.Local);
        var localEnd = DateTime.SpecifyKind(dayDate.Add(endTime), DateTimeKind.Local);

        bool isFocus = IsFocusBlock;
        var placementError = ValidateDraftPlacement(localStart, localEnd, isFocus);
        if (placementError is not null) { FormError = placementError; return false; }

        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        var columnIndex = (dayDate - WeekStart.Date).Days;
        if (columnIndex is >= 0 and <= 6)
        {
            var excludeId = _editingIsDraft ? _editingBlockId ?? Guid.Empty : Guid.Empty;
            var dayTiles = Tiles.Where(t => t.ColumnIndex == columnIndex);
            if (PlannerDragMath.WouldCollide(excludeId, isFocus, startUtc, endUtc, dayTiles))
            {
                FormError = "Focus blocks cannot overlap — choose a different time.";
                return false;
            }
        }

        if (_editingIsDraft && _editingBlockId.HasValue)
        {
            var idx = _draftBlocks.FindIndex(d => d.Id == _editingBlockId.Value);
            if (idx >= 0)
                _draftBlocks[idx] = new ContractDraftBlock(_editingBlockId.Value, SelectedPreset.Id, SelectedPreset.Name, startUtc, endUtc, isFocus);
        }
        else
        {
            _draftBlocks.Add(new ContractDraftBlock(Guid.NewGuid(), SelectedPreset.Id, SelectedPreset.Name, startUtc, endUtc, isFocus));
        }

        RebuildDraftTilesInPlace();
        return true;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_editingBlockId.HasValue) return;

        if (_editingIsDraft)
        {
            _draftBlocks.RemoveAll(d => d.Id == _editingBlockId.Value);
            RebuildDraftTilesInPlace();
            await RefreshDayFloorPreviewsAsync();
            CloseForm();
            return;
        }

        var result = await _deleteBlock.HandleAsync(new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
        if (result.Success) { await LoadWeekAsync(); CloseForm(); }
        else { FormError = result.ErrorMessage; }
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        IsStartLocked = false;
        IsEditingCompleted = false;
        IsEditingBlockEnded = false;
        IsEditingBlockContractLocked = false;
        _editingBlockId = null;
        _editingIsDraft = false;
    }

    // ── Time parsing ─────────────────────────────────────────────────────────────
    // TimeSpan.TryParse rejects hours > 23, but "24:00" is a valid end-of-day value.
    private static bool TryParseFormTime(string s, out TimeSpan result)
    {
        if (s == "24:00") { result = TimeSpan.FromHours(24); return true; }
        return TimeSpan.TryParse(s, out result);
    }

    // ── Drag lock checks (called from code-behind before starting a drag) ──────

    /// <summary>
    /// True when the tile must not be dragged at all — it is either completed
    /// or the active session's originating block. Draft blocks are never locked.
    /// </summary>
    public bool IsTileFullyLocked(Guid blockId)
    {
        if (_draftBlocks.Any(d => d.Id == blockId)) return false;
        if (!_blocksInWeek.TryGetValue(blockId, out var dto)) return true;
        return dto.IsCompleted || _sessionState.ActiveSession?.OriginatingBlockId == blockId;
    }

    /// <summary>
    /// True when the block's start time is in the past — move and top-resize
    /// are disallowed; bottom-resize on a past-start block is also disallowed.
    /// Draft blocks are never start-locked.
    /// </summary>
    public bool IsTileStartLocked(Guid blockId)
    {
        if (_draftBlocks.Any(d => d.Id == blockId)) return false;
        if (!_blocksInWeek.TryGetValue(blockId, out var dto)) return true;
        return dto.Start <= DateTime.UtcNow;
    }

    // ── Drag-create: open the form pre-filled with the dragged range ──────────

    [RelayCommand]
    private async Task OpenNewBlockFormWithRangeAsync(DragCreatedRange range)
    {
        await EnsurePresetsLoadedAsync();
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        IsStartLocked = false;
        FormTitle = "New Block";
        ResetForm();

        var blockDate = DateTime.SpecifyKind(WeekStart.AddDays(range.ColumnIndex).Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        FormStartTime = $"{(int)range.LocalStart.TotalHours:D2}:{range.LocalStart.Minutes:D2}";
        FormEndTime   = $"{(int)range.LocalEnd.TotalHours:D2}:{range.LocalEnd.Minutes:D2}";
        SelectedPreset = AvailablePresets.FirstOrDefault();
        IsFormVisible = true;
    }

    // ── Drag-move / drag-resize: commit via UpdatePlannedBlockCommand ─────────

    [RelayCommand]
    private async Task MoveOrResizeBlockAsync(DragMoveResult result)
    {
        DragError = null;

        var draftIndex = _draftBlocks.FindIndex(d => d.Id == result.BlockId);
        if (draftIndex >= 0)
        {
            var placementError = ValidateDraftPlacement(
                result.NewStartUtc.ToLocalTime(), result.NewEndUtc.ToLocalTime(), _draftBlocks[draftIndex].IsFocus);
            if (placementError is not null)
            {
                // No rebuild needed: _draftBlocks is untouched, and the code-behind's
                // ClearDragState() already removed the drag ghost and restored the real
                // tile's opacity before this command ran — Tiles already reflects the
                // unchanged, correct position.
                DragError = placementError;
                return;
            }

            _draftBlocks[draftIndex] = _draftBlocks[draftIndex] with { Start = result.NewStartUtc, End = result.NewEndUtc };
            RebuildDraftTilesInPlace();
            await RefreshDayFloorPreviewsAsync();
            return;
        }

        if (!_blocksInWeek.TryGetValue(result.BlockId, out var dto))
        {
            DragError = "Block not found — please refresh.";
            return;
        }

        if (IsTileFullyLocked(result.BlockId))
        {
            DragError = "This block is locked and cannot be moved.";
            return;
        }

        var updateResult = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
        {
            BlockId   = result.BlockId,
            PresetId = dto.PresetId,
            Start     = result.NewStartUtc,
            End       = result.NewEndUtc
        });

        if (!updateResult.Success)
            DragError = updateResult.ErrorMessage;

        // Reload either way: on success to reflect the committed change, on failure so the
        // tile snaps back to its actual (unchanged) position — the drag gesture already
        // moved it visually, and without this it would stay stuck at the rejected drop
        // point until some unrelated reload happened to fire.
        await LoadWeekAsync();
    }

    // ── Nen Contract wizard + drafting mode ───────────────────────────────────

    [RelayCommand]
    private void OpenContractWizard()
    {
        if (!CanOpenContractWizard) return;
        _editingContractPayload = null;
        IsNenContractPopoverVisible = false;
        WizardViewModel = new ContractWizardViewModel(
            getPresets: _getContractPresets,
            createPreset: _createContractPreset,
            checkOverlap: _checkOverlap,
            initialStartDate: DateOnly.FromDateTime(_clockService.LocalNow.Date),
            onReadyToSchedule: BeginDrafting,
            onCancel: CloseWizard);
        IsWizardVisible = true;
        ContractWizardOpened?.Invoke(WizardViewModel);
    }

    // Popover's Edit action on an Upcoming (Scheduled) contract. Gated the same way as
    // OpenContractWizard (overlay collision only, not session-active — see CanOpenContractWizard).
    [RelayCommand]
    private async Task RequestEditContractAsync(Guid contractId)
    {
        if (!CanOpenContractWizard) return;

        var payload = await _getContractForEdit.HandleAsync(new GetNenContractForEditQuery(contractId));
        if (payload is null) return;

        _editingContractPayload = payload;
        IsNenContractPopoverVisible = false;
        WizardViewModel = new ContractWizardViewModel(
            getPresets: _getContractPresets,
            createPreset: _createContractPreset,
            checkOverlap: _checkOverlap,
            initialStartDate: payload.StartDate,
            onReadyToSchedule: BeginDrafting,
            onCancel: CloseWizard,
            editingContract: payload);
        IsWizardVisible = true;
        ContractWizardOpened?.Invoke(WizardViewModel);
    }

    private void CloseWizard()
    {
        IsWizardVisible = false;
        WizardViewModel = null;
        ContractWizardClosed?.Invoke();
    }

    private void BeginDrafting(NenContractDraftRules rules)
    {
        _draftRules = rules;
        _draftBlocks.Clear();

        // Editing an existing Scheduled contract: seed the calendar with its already-drafted
        // blocks instead of a blank week, so "edit" actually shows what's being edited.
        if (rules.EditingContractId.HasValue && _editingContractPayload is not null)
        {
            foreach (var block in _editingContractPayload.Blocks)
            {
                _draftBlocks.Add(new ContractDraftBlock(
                    Guid.NewGuid(), block.PresetId, block.PresetName, block.Start, block.End, block.IsFocus));
            }
        }

        CloseWizard();
        IsDraftingContract = true;
        DraftContractName = rules.Name;
        SealErrorMessage = null;

        WeekStart = StartOfWeek(rules.StartDate.ToDateTime(TimeOnly.MinValue));
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        _ = LoadWeekAsync();
    }

    private void AppendDraftTiles()
    {
        foreach (var draft in _draftBlocks)
        {
            var localStart = draft.Start.ToLocalTime();
            var localEnd = draft.End.ToLocalTime();
            var dayIndex = (localStart.Date - WeekStart.Date).Days;
            if (dayIndex < 0 || dayIndex > 6) continue;

            var dayStart = WeekStart.AddDays(dayIndex);
            var visibleStart = dayStart.AddHours(FirstHour);
            var visibleEnd = dayStart.AddHours(LastHour);
            var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
            if (position is null) continue;

            var blockPresetType = draft.IsFocus ? BlockPresetType.Focus : BlockPresetType.Background;
            var syntheticDto = new PlannedBlockDto
            {
                Id = draft.Id,
                PresetId = draft.PresetId,
                PresetName = draft.PresetName,
                BlockPresetType = blockPresetType,
                Start = localStart,
                End = localEnd,
                IsCompleted = false,
                HasEnded = false
            };

            Tiles.Add(new PlannedBlockTileViewModel(
                syntheticDto, blockPresetType, dayIndex, position.Value.Top, position.Value.Height, isDraft: true));
        }
    }

    private void RebuildDraftTilesInPlace()
    {
        var nonDraftTiles = Tiles.Where(t => !t.IsDraft).ToList();
        Tiles.Clear();
        foreach (var t in nonDraftTiles) Tiles.Add(t);
        AppendDraftTiles();
    }

    // Shared by SaveDraftBlock (create/edit-via-form) and MoveOrResizeBlockAsync's draft
    // branch (drag-move/resize of an already-placed draft tile) — same rules, same
    // reject-after-the-fact mechanism every other Planner rule already uses (Focus-overlap,
    // Work Hours, past-date for ordinary blocks). Returns null when the placement is valid.
    // Work Hours mirrors NenContract.AllowsBlockPlacement (same WorkHours.ContainsRange
    // domain check the sealed-contract guard uses) so drafting and post-seal editing enforce
    // the identical rule — no NenContract exists yet during drafting to delegate to directly,
    // but the wizard's flat Default-only window (no per-day exceptions in the UI) is enough
    // to reconstruct it locally. Focus-only: Background blocks are never Work-Hours constrained.
    private string? ValidateDraftPlacement(DateTime localStart, DateTime localEnd, bool isFocus)
    {
        if (localStart <= _clockService.LocalNow)
            return "Blocks cannot start in the past.";

        if (_draftRules is not null)
        {
            var startDate = DateOnly.FromDateTime(localStart);
            var endDate = DateOnly.FromDateTime(localEnd);
            if (startDate < _draftRules.StartDate || endDate > _draftRules.EndDate)
                return $"Blocks must be placed within the contract's date range ({_draftRules.StartDate:MMM d} – {_draftRules.EndDate:MMM d}).";

            if (isFocus && _draftRules.WorkHoursStart is { } workHoursStart && _draftRules.WorkHoursEnd is { } workHoursEnd)
            {
                var workHours = new WorkHours(workHoursStart, workHoursEnd);
                if (!workHours.ContainsRange(localStart, localEnd))
                    return $"This block falls outside the contract's Work Hours ({workHoursStart:h:mm tt} – {workHoursEnd:h:mm tt}).";
            }
        }

        return null;
    }

    private async Task RefreshDayFloorPreviewsAsync()
    {
        if (_draftRules is null) { DayFloorByDate = new(); return; }

        var focusRanges = _draftBlocks
            .Where(d => d.IsFocus)
            .Select(d => (d.Start, d.End))
            .ToList();

        var hoursByDay = await _computePlannedFocusHours.HandleAsync(
            new ComputePlannedFocusHoursQuery(ContractId: null, DraftFocusBlockRanges: focusRanges));

        var byDate = new Dictionary<DateOnly, DayFloorPreviewViewModel>();
        for (int i = 0; i < 7; i++)
        {
            var date = DateOnly.FromDateTime(WeekStart.AddDays(i));
            if (date < _draftRules.StartDate || date > _draftRules.EndDate) continue;

            var scheduled = hoursByDay.GetValueOrDefault(date);
            bool isRestDay = _draftRules.FixedDailyFloorHours.HasValue
                ? _draftRules.RestDays.Contains(date)
                : scheduled <= 0;
            double? requiredFloor = _draftRules.FixedDailyFloorHours.HasValue && !isRestDay
                ? _draftRules.FixedDailyFloorHours
                : null;

            byDate[date] = new DayFloorPreviewViewModel(date, scheduled, requiredFloor, isRestDay);
        }

        DayFloorByDate = byDate;
    }

    [RelayCommand]
    private async Task SealContractAsync()
    {
        if (_draftRules is null) return;
        SealErrorMessage = null;
        IsSealing = true;
        try
        {
            var blocks = _draftBlocks
                .Select(d => new NenContractBlockSpec(d.PresetId, d.Start, d.End, d.IsFocus))
                .ToList();

            bool success;
            string? errorMessage;

            if (_draftRules.EditingContractId is { } editingContractId)
            {
                var editResult = await _editContract.HandleAsync(new EditNenContractCommand
                {
                    ContractId = editingContractId,
                    Name = _draftRules.Name,
                    Goal = _draftRules.Goal,
                    StartDate = _draftRules.StartDate,
                    EndDate = _draftRules.EndDate,
                    StrictnessRule = _draftRules.StrictnessRule,
                    AllowCancellation = _draftRules.AllowCancellation,
                    FixedDailyFloorHours = _draftRules.FixedDailyFloorHours,
                    RestDays = _draftRules.RestDays,
                    WorkHoursStart = _draftRules.WorkHoursStart,
                    WorkHoursEnd = _draftRules.WorkHoursEnd,
                    Blocks = blocks,
                    ContractPresetId = _draftRules.ContractPresetId
                });
                success = editResult.Success;
                errorMessage = editResult.ErrorMessage;
            }
            else
            {
                var sealResult = await _sealContract.HandleAsync(new SealNenContractCommand
                {
                    Name = _draftRules.Name,
                    Goal = _draftRules.Goal,
                    StartDate = _draftRules.StartDate,
                    EndDate = _draftRules.EndDate,
                    StrictnessRule = _draftRules.StrictnessRule,
                    AllowCancellation = _draftRules.AllowCancellation,
                    FixedDailyFloorHours = _draftRules.FixedDailyFloorHours,
                    RestDays = _draftRules.RestDays,
                    WorkHoursStart = _draftRules.WorkHoursStart,
                    WorkHoursEnd = _draftRules.WorkHoursEnd,
                    Blocks = blocks,
                    ContractPresetId = _draftRules.ContractPresetId
                });
                success = sealResult.Success;
                errorMessage = sealResult.ErrorMessage;
            }

            if (!success)
            {
                SealErrorMessage = errorMessage;
                return;
            }

            ExitDraftingMode();
            await LoadWeekAsync();
            await RefreshActiveContractAsync();
            await RefreshScheduledContractsAsync();
        }
        finally
        {
            IsSealing = false;
        }
    }

    [RelayCommand]
    private void RequestCancelDraft()
    {
        ConfirmDiscardDraftMessage = _draftBlocks.Count switch
        {
            0 => "Discard this contract draft?",
            1 => "Discard this contract draft and its 1 block?",
            var n => $"Discard this contract draft and its {n} blocks?"
        };
        IsConfirmDiscardDraftVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCancelDraftAsync()
    {
        IsConfirmDiscardDraftVisible = false;
        ExitDraftingMode();
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    [RelayCommand]
    private void CancelCancelDraft() => IsConfirmDiscardDraftVisible = false;

    private void ExitDraftingMode()
    {
        IsDraftingContract = false;
        DraftContractName = null;
        _draftRules = null;
        _draftBlocks.Clear();
        DayFloorByDate = new();
        SealErrorMessage = null;
        _editingContractPayload = null;
    }

    // ── Nen Contract popover ────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleNenContractPopoverAsync()
    {
        if (IsNenContractPopoverVisible)
        {
            IsNenContractPopoverVisible = false;
            return;
        }

        await RefreshScheduledContractsAsync();
        IsNenContractPopoverVisible = true;
    }

    [RelayCommand]
    private void CloseNenContractPopover() => IsNenContractPopoverVisible = false;

    private async Task RefreshScheduledContractsAsync()
    {
        var scheduled = await _getScheduledContracts.HandleAsync(new GetScheduledNenContractsQuery());
        ScheduledContracts.Clear();
        foreach (var contract in scheduled)
            ScheduledContracts.Add(contract);
        OnPropertyChanged(nameof(HasNoContracts));
    }

    [RelayCommand]
    private void RequestDeleteContract(Guid contractId)
    {
        _contractIdPendingDelete = contractId;
        DeleteContractErrorMessage = null;
        ConfirmDeleteContractMessage = "Delete this contract? This cannot be undone.";
        IsConfirmDeleteContractVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteContractAsync()
    {
        if (_contractIdPendingDelete is not { } contractId) { IsConfirmDeleteContractVisible = false; return; }

        var result = await _deleteContract.HandleAsync(new DeleteNenContractCommand { ContractId = contractId });
        IsConfirmDeleteContractVisible = false;
        _contractIdPendingDelete = null;

        if (!result.Success)
        {
            DeleteContractErrorMessage = result.ErrorMessage;
            return;
        }

        await RefreshScheduledContractsAsync();
    }

    [RelayCommand]
    private void CancelDeleteContract() => IsConfirmDeleteContractVisible = false;

    public void Dispose()
    {
        _clockService.Tick -= OnClockTick;
        _sessionState.SessionChanged -= OnSessionChanged;
        _floorStatusPollTimer.Elapsed -= OnFloorStatusPollTick;
        _floorStatusPollTimer.Dispose();
    }
}
