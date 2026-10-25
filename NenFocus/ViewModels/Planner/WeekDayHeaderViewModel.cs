using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels.Planner;

public partial class WeekDayHeaderViewModel : ViewModelBase
{
    private readonly DateTime _today;

    public DateTime Date { get; }
    public bool IsToday => Date.Date == _today.Date;

    // Set after construction by PlannerPageViewModel whenever DayFloorByDate refreshes
    // (drafting or a sealed/active contract) — null when there's no floor status for
    // this day at all (no contract in effect, outside a draft's date range, etc.).
    [ObservableProperty] private DayFloorPreviewViewModel? _dayFloorStatus;

    public WeekDayHeaderViewModel(DateTime date, DateTime today)
    {
        Date = date.Date;
        _today = today.Date;
    }
}

