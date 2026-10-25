using System;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Planner.ContractWizard;

public partial class ContractModeStepViewModel : ViewModelBase
{
    private readonly Action<bool> _onContinue;
    private readonly Action _onBack;

    public ContractModeStepViewModel(Action<bool> onContinue, Action onBack)
    {
        _onContinue = onContinue;
        _onBack = onBack;
    }

    [RelayCommand] private void ChooseSimple() => _onContinue(false);
    [RelayCommand] private void ChooseAdvanced() => _onContinue(true);
    [RelayCommand] private void Back() => _onBack();
}
