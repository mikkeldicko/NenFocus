using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels.Components;

public partial class HourLabelViewModel : ViewModelBase
{
    public int Hour { get; }
    public string Label => $"{Hour:00}:00";

    /// <summary>False for the first and last hour rows, where the label text is clipped by the grid edge.</summary>
    public bool IsIntermediate => Hour > 0 && Hour < 23;

    public HourLabelViewModel(int hour)
    {
        Hour = hour;
    }
}

