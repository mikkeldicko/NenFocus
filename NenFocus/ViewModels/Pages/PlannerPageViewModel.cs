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
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Enums;
using NenFocus.Helpers;
using NenFocus.ViewModels.Components;

namespace NenFocus.ViewModels.Pages;

public partial class PlannerPageViewModel : ViewModelBase, IDisposable
{
    // â”€â”€ Layout constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
    private readonly IPlannerClockService _clockService;

    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();
    private Guid? _editingBlockId;

    // â”€â”€ Week state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
        ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deleteBlock,
        IPlannerClockService clockService)
    {
        _getProfiles = getProfiles;
        _getBlocksForDay = getBlocksForDay;
        _createBlock = createBlock;
        _updateBlock = updateBlock;
        _deleteBlock = deleteBlock;
        _clockService = clockService;

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
        var today = StartOfWeek(_clockService.LocalNow.Date);
        if (today == WeekStart) return;
        WeekStart = today;
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        await LoadWeekAsync();
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
        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();

        var dayStart = WeekStart.AddDays(columnIndex);
        var visibleStart = dayStart.AddHours(FirstHour);
        var visibleEnd = dayStart.AddHours(LastHour);

        var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
        if (position is null) return null;

        var localDto = dto with { Start = localStart, End = localEnd };
        return new PlannedBlockTileViewModel(localDto, dto.BlockType, columnIndex, position.Value.Top, position.Value.Height);
    }

    [RelayCommand]
    private async Task NewBlockAtAsync(GridSlotPosition slot)
    {
        await EnsureProfilesLoadedAsync();
        _editingBlockId = null;
        IsEditing = false;
        FormTitle = "New Block";
        ResetForm();

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

        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();
        var blockDate = DateTime.SpecifyKind(localStart.Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        FormStartTime = $"{localStart.Hour:D2}:{localStart.Minute:D2}";
        FormEndTime = $"{localEnd.Hour:D2}:{localEnd.Minute:D2}";
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

        var dayDate = FormDate.Date;
        var localStart = DateTime.SpecifyKind(dayDate.Add(startTime), DateTimeKind.Local);
        var localEnd = DateTime.SpecifyKind(dayDate.Add(endTime), DateTimeKind.Local);
        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        var blockType = IsFocusBlock ? BlockType.Ritual : BlockType.Background;

        if (IsEditing && _editingBlockId.HasValue)
        {
            var result = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
            {
                BlockId = _editingBlockId.Value,
                ProfileId = SelectedProfile.Id,
                Start = startUtc,
                End = endUtc
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            var result = await _createBlock.HandleAsync(new CreatePlannedBlockCommand
            {
                ProfileId = SelectedProfile.Id,
                BlockType = blockType,
                Start = startUtc,
                End = endUtc
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        await LoadWeekAsync();
        CloseForm();
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
        _clockService.Tick -= OnClockTick;
    }
}