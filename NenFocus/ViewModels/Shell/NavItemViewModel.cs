using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Shell;

public partial class NavItemViewModel : ViewModelBase
{
    private readonly Action<NavItemViewModel> _onSelect;

    public NavItemViewModel(
        string label,
        string icon,
        ViewModelBase pageViewModel,
        Action<NavItemViewModel> onSelect)
    {
        Label = label;
        Icon = icon;
        PageViewModel = pageViewModel;
        _onSelect = onSelect;
    }

    public string Label { get; }
    public string Icon { get; }
    public ViewModelBase PageViewModel { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(this);
}
