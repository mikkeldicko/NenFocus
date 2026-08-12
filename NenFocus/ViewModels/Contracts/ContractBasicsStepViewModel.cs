using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;

namespace NenFocus.ViewModels.Contracts;

public partial class ContractBasicsStepViewModel : ViewModelBase
{
    private readonly IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> _checkOverlap;
    private readonly Action _onContinue;
    private readonly Action _onBack;
    private readonly Guid? _excludeContractId;

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

    // UX convenience only — SealNenContractValidator is the real guard (defense in depth).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string? _overlapErrorMessage;

    public bool CanContinue =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Goal) &&
        EndDate.Date >= StartDate.Date &&
        OverlapErrorMessage is null;

    public ContractBasicsStepViewModel(
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap,
        string initialName,
        string initialGoal,
        DateTimeOffset initialStartDate,
        DateTimeOffset initialEndDate,
        Action onContinue,
        Action onBack,
        Guid? excludeContractId = null)
    {
        _checkOverlap = checkOverlap;
        _name = initialName;
        _goal = initialGoal;
        _startDate = initialStartDate;
        _endDate = initialEndDate;
        _onContinue = onContinue;
        _onBack = onBack;
        _excludeContractId = excludeContractId;

        _ = RevalidateOverlapAsync();
    }

    partial void OnStartDateChanged(DateTimeOffset value) => _ = RevalidateOverlapAsync();
    partial void OnEndDateChanged(DateTimeOffset value) => _ = RevalidateOverlapAsync();

    private async Task RevalidateOverlapAsync()
    {
        if (EndDate.Date < StartDate.Date) { OverlapErrorMessage = null; return; }

        var overlapping = await _checkOverlap.HandleAsync(new CheckNenContractDateRangeOverlapQuery(
            DateOnly.FromDateTime(StartDate.Date), DateOnly.FromDateTime(EndDate.Date), _excludeContractId));

        OverlapErrorMessage = overlapping is not null
            ? $"Overlaps with your existing contract \"{overlapping}\"."
            : null;
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => _onContinue();

    [RelayCommand]
    private void Back() => _onBack();
}
