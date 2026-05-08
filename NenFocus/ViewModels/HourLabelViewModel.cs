using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels;

/// <summary>
/// One hour label shown on the left axis of the week grid.
/// </summary>
public partial class HourLabelViewModel : ViewModelBase
{
    public int Hour { get; }
    public string Label => $"{Hour:00}:00";

    public HourLabelViewModel(int hour)
    {
        Hour = hour;
    }
}
