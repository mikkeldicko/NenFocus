using System;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Ritual;

public partial class TenPageViewModel : ViewModelBase
{
    private readonly Action _onContinue;
    private readonly Action _onBack;

    public TenPageViewModel(Action onContinue, Action onBack)
    {
        _onContinue = onContinue;
        _onBack = onBack;
    }

    [RelayCommand]
    private void Continue() => _onContinue();

    [RelayCommand]
    private void Back() => _onBack();
}

