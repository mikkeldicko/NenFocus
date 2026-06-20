using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Header for a single day column (Monâ€“Sun) at the top of the week grid.
/// </summary>
public partial class WeekDayHeaderViewModel : ViewModelBase
{
    public DateTime Date { get; }
    public string DayName => Date.ToString("ddd").ToUpperInvariant();
    public string DayNumber => Date.Day.ToString();
    public bool IsToday => Date.Date == DateTime.Today;

    public WeekDayHeaderViewModel(DateTime date)
    {
        Date = date.Date;
    }
}

