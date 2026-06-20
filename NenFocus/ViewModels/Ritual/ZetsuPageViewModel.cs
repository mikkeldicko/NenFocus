using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Ritual;

public partial class ZetsuPageViewModel : ViewModelBase
{
    private readonly Action _onContinue;
    private readonly Action _onBack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _goal = string.Empty;

    public bool CanContinue => !string.IsNullOrWhiteSpace(Goal);

    public ZetsuPageViewModel(Action onContinue, Action onBack)
    {
        _onContinue = onContinue;
        _onBack = onBack;
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => _onContinue();

    [RelayCommand]
    private void Back()
    {
        Goal = string.Empty;
        _onBack();
    }
}

