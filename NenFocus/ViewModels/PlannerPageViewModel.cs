using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels;

public partial class PlannerPageViewModel : ViewModelBase
{
    // ── Layout constants ──────────────────────────────────────────────────
    public const double HourRowHeight = 56.0;
    public const int FirstHour = 0;
    public const int LastHour = 24; // exclusive
    public const int TotalHours = LastHour - FirstHour;
    public double GridContentHeight => HourRowHeight * TotalHours;

    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> _getProfiles;
    private readonly IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> _getBlocksForDay;
    private readonly ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> _createBlock;
    private readonly ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> _updateBlock;
    private readonly ICommandHandler<DeletePlannedBlockCommand> _deleteBlock;

    /// <summary>Cache of loaded blocks for the visible week, keyed by id.</summary>
    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();

    private Guid? _editingBlockId;

    // ── Week state ────────────────────────────────────────────────────────
    [ObservableProperty] private DateTime _weekStart;

    public string WeekRangeText
    {
        get
        {
            var end = WeekStart.AddDays(6);
            return WeekStart.Month == end.Month
                ? $"{WeekStart:MMM d} – {end.Day}, {end.Year}"
                : $"{WeekStart:MMM d} – {end:MMM d}, {end.Year}";
        }
    }

    public ObservableCollection<WeekDayHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<HourLabelViewModel> HourLabels { get; } = new();
    public ObservableCollection<PlannedBlockTileViewModel> Tiles { get; } = new();

    [ObservableProperty] private bool _isLoading;

    // ── Modal: create / edit ──────────────────────────────────────────────
    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Block";
    [ObservableProperty] private string? _formError;

    [ObservableProperty] private DateTimeOffset _formDate;
    [ObservableProperty] private int _formStartHour;
    [ObservableProperty] private int _formEndHour;
    [ObservableProperty] private bool _isFocusBlock = true;
    [ObservableProperty] private BlockProfileDto? _selectedProfile;

    public ObservableCollection<BlockProfileDto> AvailableProfiles { get; } = new();
    public ObservableCollection<int> HourOptions { get; } = new();

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Block";
    public bool CanDeleteFromForm => IsEditing;
    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(CanDeleteFromForm));
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public PlannerPageViewModel(
        IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> getProfiles,
        IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
        ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createBlock,
        ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updateBlock,
        ICommandHandler<DeletePlannedBlockCommand> deleteBlock)
    {
        _getProfiles = getProfiles;
        _getBlocksForDay = getBlocksForDay;
        _createBlock = createBlock;
        _updateBlock = updateBlock;
        _deleteBlock = deleteBlock;

        for (int h = FirstHour; h < LastHour; h++)
            HourLabels.Add(new HourLabelViewModel(h));

        for (int h = 0; h <= 24; h++)
            HourOptions.Add(h);

        WeekStart = StartOfWeek(DateTime.Today);
        BuildDayHeaders();

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadProfilesAsync();
        await LoadWeekAsync();
    }

    // ── Week navigation ───────────────────────────────────────────────────
    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        WeekStart = WeekStart.AddDays(-7);
        BuildDayHeaders();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        WeekStart = WeekStart.AddDays(7);
        BuildDayHeaders();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        var today = StartOfWeek(DateTime.Today);
        if (today == WeekStart) return;
        WeekStart = today;
        BuildDayHeaders();
        await LoadWeekAsync();
    }

    partial void OnWeekStartChanged(DateTime value) =>
        OnPropertyChanged(nameof(WeekRangeText));

    private void BuildDayHeaders()
    {
        DayHeaders.Clear();
        for (int i = 0; i < 7; i++)
            DayHeaders.Add(new WeekDayHeaderViewModel(WeekStart.AddDays(i)));
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        // Monday-based week.
        int diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }

    // ── Loading ───────────────────────────────────────────────────────────
    private async Task LoadProfilesAsync()
    {
        var profiles = await _getProfiles.HandleAsync(new GetAllBlockProfilesQuery());
        AvailableProfiles.Clear();
        foreach (var p in profiles) AvailableProfiles.Add(p);
    }

    private async Task LoadWeekAsync()
    {
        IsLoading = true;
        Tiles.Clear();
        _blocksInWeek.Clear();

        for (int i = 0; i < 7; i++)
        {
            var day = WeekStart.AddDays(i);
            var query = new GetPlannedBlocksForDayQuery
            {
                Date = DateOnly.FromDateTime(day)
            };
            var blocks = await _getBlocksForDay.HandleAsync(query);

            foreach (var b in blocks)
            {
                _blocksInWeek[b.Id] = b;
                var tile = BuildTile(b, i);
                if (tile is not null)
                    Tiles.Add(tile);
            }
        }

        IsLoading = false;
    }

    /// <summary>
    /// Compute a tile's vertical layout for the visible hour range.
    /// Returns null if the block falls entirely outside the visible band.
    /// </summary>
    private PlannedBlockTileViewModel? BuildTile(PlannedBlockDto dto, int columnIndex)
    {
        var dayStart = WeekStart.AddDays(columnIndex);
        var visibleStart = dayStart.AddHours(FirstHour);
        var visibleEnd = dayStart.AddHours(LastHour);

        var clippedStart = dto.Start < visibleStart ? visibleStart : dto.Start;
        var clippedEnd = dto.End > visibleEnd ? visibleEnd : dto.End;

        if (clippedEnd <= clippedStart) return null;

        var startMinutes = (clippedStart - visibleStart).TotalMinutes;
        var durationMinutes = (clippedEnd - clippedStart).TotalMinutes;

        var top = (startMinutes / 60.0) * HourRowHeight;
        var height = Math.Max(HourRowHeight * 0.5, (durationMinutes / 60.0) * HourRowHeight);

        var blockType = ParseBlockType(dto.BlockType);
        return new PlannedBlockTileViewModel(dto, blockType, columnIndex, top, height);
    }

    private static BlockType ParseBlockType(string raw) =>
        Enum.TryParse<BlockType>(raw, ignoreCase: true, out var bt) ? bt : BlockType.Focus;

    // ── Modal: open for create ────────────────────────────────────────────
    [RelayCommand]
    private async Task NewBlockAtAsync(GridSlotPosition slot)
    {
        await EnsureProfilesLoadedAsync();
        _editingBlockId = null;
        IsEditing = false;
        FormTitle = "New Block";
        ResetForm();
        FormDate = new DateTimeOffset(
            DateTime.SpecifyKind(WeekStart.AddDays(slot.ColumnIndex).Date, DateTimeKind.Unspecified),
            TimeSpan.Zero);
        FormStartHour = Math.Clamp(slot.Hour, FirstHour, LastHour - 1);
        FormEndHour = Math.Clamp(slot.Hour + 1, FirstHour + 1, LastHour);
        SelectedProfile = AvailableProfiles.FirstOrDefault();
        IsFormVisible = true;
    }

    // ── Modal: open for edit ──────────────────────────────────────────────
    [RelayCommand]
    private async Task EditBlockAsync(PlannedBlockTileViewModel tile)
    {
        if (!_blocksInWeek.TryGetValue(tile.Id, out var dto)) return;
        await EnsureProfilesLoadedAsync();

        _editingBlockId = dto.Id;
        IsEditing = true;
        FormTitle = "Edit Block";
        ResetForm();
        FormDate = new DateTimeOffset(
            DateTime.SpecifyKind(dto.Start.Date, DateTimeKind.Unspecified),
            TimeSpan.Zero);
        FormStartHour = dto.Start.Hour;
        FormEndHour = dto.End.Date > dto.Start.Date
            ? 24
            : dto.End.Hour + (dto.End.Minute > 0 ? 1 : 0);
        IsFocusBlock = ParseBlockType(dto.BlockType) == BlockType.Focus;
        SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.Id == dto.ProfileId)
                          ?? AvailableProfiles.FirstOrDefault();
        IsFormVisible = true;
    }

    private async Task EnsureProfilesLoadedAsync()
    {
        if (AvailableProfiles.Count == 0)
            await LoadProfilesAsync();
    }

    private void ResetForm()
    {
        FormError = null;
        IsFocusBlock = true;
        SelectedProfile = null;
    }

    // ── Block type toggle ─────────────────────────────────────────────────
    [RelayCommand] private void SetFocusBlock() => IsFocusBlock = true;
    [RelayCommand] private void SetPassiveBlock() => IsFocusBlock = false;

    // ── Save ──────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;

        if (SelectedProfile is null)
        {
            FormError = "Please select a block profile.";
            return;
        }

        if (FormEndHour <= FormStartHour)
        {
            FormError = "End time must be after start time.";
            return;
        }

        var dayDate = FormDate.Date;
        var start = dayDate.AddHours(FormStartHour);
        var end = dayDate.AddHours(FormEndHour);
        var blockType = IsFocusBlock ? BlockType.Focus : BlockType.Passive;

        // Client-side overlap guard for Focus blocks (mirrors server validator).
        if (blockType == BlockType.Focus && HasOverlappingFocusBlock(start, end, _editingBlockId))
        {
            FormError = "This Focus block overlaps with an existing Focus block.";
            return;
        }

        if (IsEditing && _editingBlockId.HasValue)
        {
            // Note: UpdatePlannedBlockCommand intentionally has no BlockType field —
            // the entity preserves its original type. To change type, delete and recreate.
            var result = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
            {
                BlockId = _editingBlockId.Value,
                ProfileId = SelectedProfile.Id,
                Start = start,
                End = end
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            var result = await _createBlock.HandleAsync(new CreatePlannedBlockCommand
            {
                ProfileId = SelectedProfile.Id,
                BlockType = blockType,
                Start = start,
                End = end
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }

        await LoadWeekAsync();
        CloseForm();
    }

    private bool HasOverlappingFocusBlock(DateTime start, DateTime end, Guid? excludeId)
    {
        foreach (var b in _blocksInWeek.Values)
        {
            if (ParseBlockType(b.BlockType) != BlockType.Focus) continue;
            if (excludeId.HasValue && b.Id == excludeId.Value) continue;
            if (start < b.End && b.Start < end) return true;
        }
        return false;
    }

    // ── Delete ────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_editingBlockId.HasValue) return;
        await _deleteBlock.HandleAsync(new DeletePlannedBlockCommand
        {
            BlockId = _editingBlockId.Value
        });
        await LoadWeekAsync();
        CloseForm();
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        _editingBlockId = null;
    }
}

/// <summary>
/// Carries (column, hour) from a clicked grid cell into the page VM's create command.
/// </summary>
public readonly record struct GridSlotPosition(int ColumnIndex, int Hour);