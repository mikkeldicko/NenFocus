using CommunityToolkit.Mvvm.ComponentModel;


namespace NenFocus.ViewModels.Planner;

public partial class HourLabelViewModel : ViewModelBase
{
    public int Hour { get; }
    public string Label => $"{Hour:00}:00";

    public bool IsIntermediate => true;

    public HourLabelViewModel(int hour)
    {
        Hour = hour;
    }
}

