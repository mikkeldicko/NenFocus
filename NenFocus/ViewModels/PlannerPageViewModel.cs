using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
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

public partial class PlannerPageViewModel : ViewModelBase, IDisposable
{
    // ── Layout constants ──────────────────────────────────────────────────
    public const double HourRowHeight = 56.0;
    public const int FirstHour = 0;
    public const int LastHour = 24;
    public const int TotalHours = LastHour - FirstHour;
    public double GridContentHeight => HourRowHeight * TotalHours;

    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> _getProfiles;
    private readonly IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> _getBlocksForDay;
    private readonly ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> _createBlock;
    private readonly ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> _updateBlock;
    private readonly ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> _deleteBlock;

    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();
    private Guid? _editingBlockId;
    private Timer? _currentTimeTimer;

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

    /// <summary>
    /// Returns the Grid.Column index for the red "Current Time" line.
    /// Column 0 is hour labels, so Column 1 is Monday, Column 7 is Sunday.
    /// </summary>
    public int TodayColumnIndex
    {
        get
        {
            int day = (int)DateTime.Now.DayOfWeek;
            int mondayBasedIndex = (day == 0) ? 6 : day - 1;
            return mondayBasedIndex + 1;
        }
    }

    public ObservableCollection<WeekDayHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<HourLabelViewModel> HourLabels { get; } = new();
    public ObservableCollection<PlannedBlockTileViewModel> Tiles { get; } = new();
    public ObservableCollection<BlockProfileDto> AvailableProfiles { get; } = new();
    public ObservableCollection<string> TimeOptions { get; } = new();

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
    [ObservableProperty] private BlockProfileDto? _selectedProfile;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Block";
    public bool CanDeleteFromForm => IsEditing;

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(CanDeleteFromForm));
    }

    public PlannerPageViewModel(
        IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> getProfiles,
        IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
        ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createBlock,
        ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updateBlock,
        ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deleteBlock)
    {
        _getProfiles = getProfiles;
        _getBlocksForDay = getBlocksForDay;
        _createBlock = createBlock;
        _updateBlock = updateBlock;
        _deleteBlock = deleteBlock;

        for (int h = FirstHour; h < LastHour; h++)
            HourLabels.Add(new HourLabelViewModel(h));

        GenerateTimeOptions();

        WeekStart = StartOfWeek(DateTime.Today);
        BuildDayHeaders();

        SetupCurrentTimeIndicator();
        _ = InitAsync();
    }

    private void SetupCurrentTimeIndicator()
    {
        _currentTimeTimer = new Timer(60000);
        _currentTimeTimer.Elapsed += (s, e) => UpdateCurrentTimePosition();
        _currentTimeTimer.AutoReset = true;
        _currentTimeTimer.Enabled = true;
        UpdateCurrentTimePosition();
    }

    private void UpdateCurrentTimePosition()
    {
        var now = DateTime.Now;
        var weekEnd = WeekStart.AddDays(7);
        IsTodayInView = now.Date >= WeekStart.Date && now.Date < weekEnd.Date;

        if (IsTodayInView)
        {
            double totalMinutes = (now.Hour - FirstHour) * 60 + now.Minute;
            CurrentTimeY = (totalMinutes / 60.0) * HourRowHeight;
        }

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
        await LoadProfilesAsync();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        WeekStart = WeekStart.AddDays(-7);
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        WeekStart = WeekStart.AddDays(7);
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        var today = StartOfWeek(DateTime.Today);
        if (today == WeekStart) return;
        WeekStart = today;
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        await LoadWeekAsync();
    }

    partial void OnWeekStartChanged(DateTime value) => OnPropertyChanged(nameof(WeekRangeText));

    private void BuildDayHeaders()
    {
        DayHeaders.Clear();
        for (int i = 0; i < 7; i++)
            DayHeaders.Add(new WeekDayHeaderViewModel(WeekStart.AddDays(i)));
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }

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
            var query = new GetPlannedBlocksForDayQuery { Date = DateOnly.FromDateTime(day) };
            var blocks = await _getBlocksForDay.HandleAsync(query);

            foreach (var b in blocks)
            {
                _blocksInWeek[b.Id] = b;
                var tile = BuildTile(b, i);
                if (tile is not null) Tiles.Add(tile);
            }
        }
        IsLoading = false;
    }

    private PlannedBlockTileViewModel? BuildTile(PlannedBlockDto dto, int columnIndex)
    {
        // Convert UTC times from DB to Local time for the UI
        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();

        var dayStart = WeekStart.AddDays(columnIndex);
        var visibleStart = dayStart.AddHours(FirstHour);
        var visibleEnd = dayStart.AddHours(LastHour);

        // Use the local times for clipping and positioning
        var clippedStart = localStart < visibleStart ? visibleStart : localStart;
        var clippedEnd = localEnd > visibleEnd ? visibleEnd : localEnd;

        if (clippedEnd <= clippedStart) return null;

        var startMinutes = (clippedStart - visibleStart).TotalMinutes;
        var durationMinutes = (clippedEnd - clippedStart).TotalMinutes;

        var top = (startMinutes / 60.0) * HourRowHeight;
        var height = Math.Max(HourRowHeight * 0.5, (durationMinutes / 60.0) * HourRowHeight);

        // Create a new DTO with local times so the Tile VM shows correct labels
        var localDto = dto with { Start = localStart, End = localEnd };

        return new PlannedBlockTileViewModel(localDto, dto.BlockType, columnIndex, top, height);
    }

    [RelayCommand]
    private async Task NewBlockAtAsync(GridSlotPosition slot)
    {
        await EnsureProfilesLoadedAsync();
        _editingBlockId = null;
        IsEditing = false;
        FormTitle = "New Block";
        ResetForm();

        // Fix: Explicitly specify UTC kind to match TimeSpan.Zero
        var blockDate = DateTime.SpecifyKind(WeekStart.AddDays(slot.ColumnIndex).Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        FormStartTime = $"{slot.Hour:D2}:00";
        FormEndTime = $"{(slot.Hour + 1):D2}:00";
        SelectedProfile = AvailableProfiles.FirstOrDefault();
        IsFormVisible = true;
    }

    [RelayCommand]
    private async Task EditBlockAsync(PlannedBlockTileViewModel tile)
    {
        if (!_blocksInWeek.TryGetValue(tile.Id, out var dto)) return;
        await EnsureProfilesLoadedAsync();
        _editingBlockId = dto.Id;
        IsEditing = true;
        FormTitle = "Edit Block";
        ResetForm();

        // Fix: Explicitly specify UTC kind to match TimeSpan.Zero for Edit as well
        var blockDate = DateTime.SpecifyKind(dto.Start.Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        FormStartTime = dto.Start.ToString("HH:mm");
        FormEndTime = dto.End.ToString("HH:mm");
        IsFocusBlock = dto.BlockType == BlockType.Ritual;
        SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.Id == dto.ProfileId)
                          ?? AvailableProfiles.FirstOrDefault();
        IsFormVisible = true;
    }

    private async Task EnsureProfilesLoadedAsync()
    {
        if (AvailableProfiles.Count == 0) await LoadProfilesAsync();
    }

    private void ResetForm()
    {
        FormError = null;
        IsFocusBlock = true;
        SelectedProfile = null;
    }

    [RelayCommand] private void SetFocusBlock() => IsFocusBlock = true;
    [RelayCommand] private void SetPassiveBlock() => IsFocusBlock = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        if (SelectedProfile is null) { FormError = "Please select a block profile."; return; }
        if (!TimeSpan.TryParse(FormStartTime, out var startTime) || !TimeSpan.TryParse(FormEndTime, out var endTime))
        {
            FormError = "Invalid time format. Use HH:mm.";
            return;
        }
        if (endTime <= startTime) { FormError = "End time must be after start time."; return; }

        // 1. Get the date from the form
        var dayDate = FormDate.Date;

        // 2. Create local DateTimes (Assume the user entered these in their local time)
        var localStart = DateTime.SpecifyKind(dayDate.Add(startTime), DateTimeKind.Local);
        var localEnd = DateTime.SpecifyKind(dayDate.Add(endTime), DateTimeKind.Local);

        // 3. Convert to UTC before saving to DB
        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        var blockType = IsFocusBlock ? BlockType.Ritual : BlockType.Background;

        if (blockType == BlockType.Ritual && HasOverlappingFocusBlock(startUtc, endUtc, _editingBlockId))
        {
            FormError = "This Focus block overlaps with an existing Focus block.";
            return;
        }

        if (IsEditing && _editingBlockId.HasValue)
        {
            var result = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
            {
                BlockId = _editingBlockId.Value,
                ProfileId = SelectedProfile.Id,
                Start = startUtc, // Save UTC
                End = endUtc     // Save UTC
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            var result = await _createBlock.HandleAsync(new CreatePlannedBlockCommand
            {
                ProfileId = SelectedProfile.Id,
                BlockType = blockType,
                Start = startUtc, // Save UTC
                End = endUtc     // Save UTC
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
            if (b.BlockType != BlockType.Ritual) continue;
            if (excludeId.HasValue && b.Id == excludeId.Value) continue;
            if (start < b.End && b.Start < end) return true;
        }
        return false;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_editingBlockId.HasValue) return;
        var result = await _deleteBlock.HandleAsync(new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
        if (result.Success) { await LoadWeekAsync(); CloseForm(); }
        else { FormError = result.ErrorMessage; }
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        _editingBlockId = null;
    }

    public void Dispose()
    {
        _currentTimeTimer?.Stop();
        _currentTimeTimer?.Dispose();
    }
}