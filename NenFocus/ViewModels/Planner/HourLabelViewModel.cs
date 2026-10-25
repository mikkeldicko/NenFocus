using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels.Planner;

public partial class HourLabelViewModel : ViewModelBase
{
    public int Hour { get; }
    /// <summary>The row's time of day — shown as "09:00" by TimeOfDayToStringConverter.</summary>
    public TimeSpan Time => TimeSpan.FromHours(Hour);

    public bool IsIntermediate => true;

    public HourLabelViewModel(int hour)
    {
        Hour = hour;
    }
}

