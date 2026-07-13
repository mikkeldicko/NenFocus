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
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Enums;
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
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _computePlannedFocusHours;
    private readonly IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> _getContractPresets;
    private readonly ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> _createContractPreset;
    private readonly IPlannerClockService _clockService;
    private readonly ISessionStateService _sessionState;

    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();
    private readonly List<BlockPresetDto> _allPresets = new();
    private readonly List<ContractDraftBlock> _draftBlocks = new();
    private Guid? _editingBlockId;
    private bool _editingIsDraft;
    private NenContractDraftRules? _draftRules;

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
    public ObservableCollection<DayFloorPreviewViewModel> DayFloorPreviews { get; } = new();

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

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Block";

    // Mirrors DeletePlannedBlockValidator's two rejection conditions exactly — sourced from
    // dto.HasEnded (Application-computed, same PlannedBlock.HasEnded the validator calls) and
    // the live active-session noticeboard, not re-derived from raw Start/End here.
    public bool CanDeleteFromForm =>
        IsEditing && !_isEditingBlockEnded && _sessionState.ActiveSession?.OriginatingBlockId != _editingBlockId;

    // True when the editing block is either currently running (active session) or already completed.
    // Controls visibility of the Save button and disables form inputs.
    public bool IsBlockLocked =>
        IsEditing && (_isEditingCompleted || _sessionState.ActiveSession?.OriginatingBlockId == _editingBlockId);

    public bool CanSaveBlock => !IsBlockLocked;

    public bool CanOpenContractWizard =>
        !IsFormVisible && !IsWizardVisible && !IsDraftingContract && _sessionState.ActiveSession is null;

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
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours,
        IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> getContractPresets,
        ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> createContractPreset,
        IPlannerClockService clockService,
        ISessionStateService sessionState)
    {
        _getPresets = getPresets;
        _getBlocksForDay = getBlocksForDay;
        _createBlock = createBlock;
        _updateBlock = updateBlock;
        _deleteBlock = deleteBlock;
        _sealContract = sealContract;
        _computePlannedFocusHours = computePlannedFocusHours;
        _getContractPresets = getContractPresets;
        _createContractPreset = createContractPreset;
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

        _ = InitAsync();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateCurrentTimePosition);
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
                _ = LoadWeekAsync();
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
            SaveDraftBlock();
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
    // repository call, nothing persists until the contract is sealed.
    private void SaveDraftBlock()
    {
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
        bool isFocus = IsFocusBlock;

        var columnIndex = (dayDate - WeekStart.Date).Days;
        if (columnIndex is >= 0 and <= 6)
        {
            var excludeId = _editingIsDraft ? _editingBlockId ?? Guid.Empty : Guid.Empty;
            var dayTiles = Tiles.Where(t => t.ColumnIndex == columnIndex);
            if (PlannerDragMath.WouldCollide(excludeId, isFocus, startUtc, endUtc, dayTiles))
            {
                FormError = "Focus blocks cannot overlap — choose a different time.";
                return;
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
        {
            DragError = updateResult.ErrorMessage;
            return;
        }

        await LoadWeekAsync();
    }

    // ── Nen Contract wizard + drafting mode ───────────────────────────────────

    [RelayCommand]
    private void OpenContractWizard()
    {
        if (!CanOpenContractWizard) return;
        WizardViewModel = new ContractWizardViewModel(
            getPresets: _getContractPresets,
            createPreset: _createContractPreset,
            initialStartDate: DateOnly.FromDateTime(_clockService.LocalNow.Date),
            onReadyToSchedule: BeginDrafting,
            onCancel: CloseWizard);
        IsWizardVisible = true;
    }

    private void CloseWizard()
    {
        IsWizardVisible = false;
        WizardViewModel = null;
    }

    private void BeginDrafting(NenContractDraftRules rules)
    {
        _draftRules = rules;
        _draftBlocks.Clear();
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

    private async Task RefreshDayFloorPreviewsAsync()
    {
        DayFloorPreviews.Clear();
        if (_draftRules is null) return;

        var focusRanges = _draftBlocks
            .Where(d => d.IsFocus)
            .Select(d => (d.Start, d.End))
            .ToList();

        var hoursByDay = await _computePlannedFocusHours.HandleAsync(
            new ComputePlannedFocusHoursQuery(ContractId: null, DraftFocusBlockRanges: focusRanges));

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

            DayFloorPreviews.Add(new DayFloorPreviewViewModel(date, scheduled, requiredFloor, isRestDay));
        }
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

            var command = new SealNenContractCommand
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
            };

            var result = await _sealContract.HandleAsync(command);
            if (!result.Success)
            {
                SealErrorMessage = result.ErrorMessage;
                return;
            }

            ExitDraftingMode();
            await LoadWeekAsync();
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
    }

    [RelayCommand]
    private void CancelCancelDraft() => IsConfirmDiscardDraftVisible = false;

    private void ExitDraftingMode()
    {
        IsDraftingContract = false;
        DraftContractName = null;
        _draftRules = null;
        _draftBlocks.Clear();
        DayFloorPreviews.Clear();
        SealErrorMessage = null;
    }

    public void Dispose()
    {
        _clockService.Tick -= OnClockTick;
        _sessionState.SessionChanged -= OnSessionChanged;
    }
}
