using CommunityToolkit.Mvvm.ComponentModel;

namespace NenFocus.ViewModels;

public partial class ChecklistItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private string _text = string.Empty;

    public ChecklistItemViewModel(string text)
    {
        Text = text;
    }
}
