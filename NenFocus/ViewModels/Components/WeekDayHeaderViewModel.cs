using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels.Components;

public partial class WeekDayHeaderViewModel : ViewModelBase
{
    private readonly DateTime _today;

    public DateTime Date { get; }
    public bool IsToday => Date.Date == _today.Date;

    public WeekDayHeaderViewModel(DateTime date, DateTime today)
    {
        Date = date.Date;
        _today = today.Date;
    }
}

