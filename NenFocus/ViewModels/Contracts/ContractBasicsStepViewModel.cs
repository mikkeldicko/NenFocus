using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NenFocus.ViewModels.Contracts;

public partial class ContractBasicsStepViewModel : ViewModelBase
{
    private readonly Action _onContinue;
    private readonly Action _onBack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _goal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private DateTimeOffset _startDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private DateTimeOffset _endDate;

    public bool CanContinue =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Goal) &&
        EndDate.Date >= StartDate.Date;

    public ContractBasicsStepViewModel(
        string initialName,
        string initialGoal,
        DateTimeOffset initialStartDate,
        DateTimeOffset initialEndDate,
        Action onContinue,
        Action onBack)
    {
        _name = initialName;
        _goal = initialGoal;
        _startDate = initialStartDate;
        _endDate = initialEndDate;
        _onContinue = onContinue;
        _onBack = onBack;
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => _onContinue();

    [RelayCommand]
    private void Back() => _onBack();
}
