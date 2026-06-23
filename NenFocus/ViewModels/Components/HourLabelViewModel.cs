using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels.Components;

public partial class HourLabelViewModel : ViewModelBase
{
    public int Hour { get; }
    public string Label => $"{Hour:00}:00";

    public HourLabelViewModel(int hour)
    {
        Hour = hour;
    }
}

