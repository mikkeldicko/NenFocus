using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Components;

namespace NenFocus.ViewModels.Contracts;

/// <summary>
/// Rules-collection shell for a new Nen Contract. Mirrors RitualShellViewModel's mechanics
/// exactly: step index + dots, a single reassigned back-action, and pull-based assembly of
/// prior steps' values into the next step's constructor. On completion (the terminal Review
/// step's "Begin Scheduling"), hands an assembled NenContractDraftRules back to the caller —
/// it does not seal anything itself; sealing happens later, after the Planner drafting step.
/// </summary>
public partial class ContractWizardViewModel : ViewModelBase
{
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly Action<NenContractDraftRules> _onReadyToSchedule;
    private readonly Action _onCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDot0Active))]
    [NotifyPropertyChangedFor(nameof(IsDot1Active))]
    [NotifyPropertyChangedFor(nameof(IsDot2Active))]
    [NotifyPropertyChangedFor(nameof(IsDot3Active))]
    private int _stepIndex;

    [ObservableProperty]
    private ViewModelBase _currentStep = null!;

    [ObservableProperty]
    private bool _isBackVisible = true;

    private Action _currentBackAction = () => { };

    public bool IsDot0Active => StepIndex == 0;
    public bool IsDot1Active => StepIndex == 1;
    public bool IsDot2Active => StepIndex == 2;
    public bool IsDot3Active => StepIndex == 3;

    private string _name = string.Empty;
    private string _goal = string.Empty;
    private DateOnly _startDate;
    private DateOnly _endDate;
    private bool _isAdvanced;

    // Non-null when this wizard instance is editing an existing Scheduled contract rather
    // than creating a new one — carried through to the assembled NenContractDraftRules so
    // PlannerPageViewModel knows to dispatch EditNenContractCommand (replace in place)
    // instead of SealNenContractCommand at the end of drafting.
    private readonly NenContractEditDto? _editingContract;

    private ContractBasicsStepViewModel? _basicsVm;
    private ContractModeStepViewModel? _modeVm;
    private ContractRulesStepViewModel? _rulesVm;

    public ContractWizardViewModel(
        IQueryDispatcher queryDispatcher,
        DateOnly initialStartDate,
        Action<NenContractDraftRules> onReadyToSchedule,
        Action onCancel,
        NenContractEditDto? editingContract = null)
    {
        _queryDispatcher = queryDispatcher;
        _onReadyToSchedule = onReadyToSchedule;
        _onCancel = onCancel;
        _editingContract = editingContract;

        if (editingContract is not null)
        {
            _name = editingContract.Name;
            _goal = editingContract.Goal;
            _startDate = editingContract.StartDate;
            _endDate = editingContract.EndDate;
            _isAdvanced = editingContract.IsAdvanced;
        }
        else
        {
            _startDate = initialStartDate;
            _endDate = initialStartDate.AddDays(6);
        }

        NavigateToBasics();
    }

    [RelayCommand] private void GoBack() => _currentBackAction();

    private void NavigateToBasics()
    {
        StepIndex = 0;
        IsBackVisible = true;
        _currentBackAction = _onCancel;
        _basicsVm = new ContractBasicsStepViewModel(
            queryDispatcher: _queryDispatcher,
            initialName: _name,
            initialGoal: _goal,
            initialStartDate: new DateTimeOffset(_startDate.ToDateTime(TimeOnly.MinValue)),
            initialEndDate: new DateTimeOffset(_endDate.ToDateTime(TimeOnly.MinValue)),
            onContinue: NavigateToMode,
            onBack: _onCancel,
            excludeContractId: _editingContract?.Id);
        CurrentStep = _basicsVm;
    }

    private void NavigateToMode()
    {
        if (_basicsVm is not null)
        {
            _name = _basicsVm.Name;
            _goal = _basicsVm.Goal;
            _startDate = DateOnly.FromDateTime(_basicsVm.StartDate.Date);
            _endDate = DateOnly.FromDateTime(_basicsVm.EndDate.Date);
        }

        StepIndex = 1;
        IsBackVisible = true;
        _modeVm = new ContractModeStepViewModel(
            onContinue: isAdvanced =>
            {
                _isAdvanced = isAdvanced;
                NavigateToRules();
            },
            onBack: NavigateToBasics);
        _currentBackAction = NavigateToBasics;
        CurrentStep = _modeVm;
    }

    private void NavigateToRules()
    {
        StepIndex = 2;
        _rulesVm = new ContractRulesStepViewModel(
            isAdvanced: _isAdvanced,
            startDate: _startDate,
            endDate: _endDate,
            queryDispatcher: _queryDispatcher,
            onContinue: NavigateToReview,
            onBack: NavigateToMode,
            initialStrictnessRule: _editingContract?.StrictnessRule,
            initialFixedDailyFloorHours: _editingContract?.FixedDailyFloorHours,
            initialAllowCancellation: _editingContract?.AllowCancellation ?? true,
            initialWorkHoursStart: _editingContract?.WorkHoursStart,
            initialWorkHoursEnd: _editingContract?.WorkHoursEnd,
            initialRestDays: _editingContract?.RestDays,
            initialContractPresetId: _editingContract?.ContractPresetId,
            initialRedemptionPlacementPolicy: _editingContract?.RedemptionPlacementPolicy);
        _currentBackAction = NavigateToMode;
        CurrentStep = _rulesVm;
    }

    private void NavigateToReview()
    {
        var rules = _rulesVm;

        var strictness = rules?.ResolvedStrictnessRule ?? StrictnessRule.DailyFlexibility;
        var allowCancellation = rules?.ResolvedAllowCancellation ?? true;
        var fixedFloor = rules?.ResolvedFixedDailyFloorHours;
        var restDays = rules?.ResolvedRestDays ?? (IReadOnlyList<DateOnly>)[];
        var workHoursStart = rules?.ResolvedWorkHoursStart;
        var workHoursEnd = rules?.ResolvedWorkHoursEnd;
        var contractPresetId = rules?.ResolvedContractPresetId;
        var redemptionPlacementPolicy = rules?.ResolvedRedemptionPlacementPolicy ?? RedemptionPlacementPolicy.AnywhereInContract;
        var modeLabel = rules?.ResolvedSummaryLabel ?? "Simple";

        StepIndex = 3;
        _currentBackAction = NavigateToRules;
        CurrentStep = new ContractReviewStepViewModel(
            getData: () => new NenContractDraftRules
            {
                Name = _name,
                Goal = _goal,
                StartDate = _startDate,
                EndDate = _endDate,
                StrictnessRule = strictness,
                AllowCancellation = allowCancellation,
                FixedDailyFloorHours = fixedFloor,
                RestDays = restDays,
                WorkHoursStart = workHoursStart,
                WorkHoursEnd = workHoursEnd,
                ContractPresetId = contractPresetId,
                RedemptionPlacementPolicy = redemptionPlacementPolicy,
                EditingContractId = _editingContract?.Id
            },
            summaryName: _name,
            summaryDateRange: $"{_startDate:MMM d, yyyy} – {_endDate:MMM d, yyyy}",
            summaryMode: modeLabel,
            summaryStrictness: FriendlyStrictnessLabel(strictness),
            summaryFloor: fixedFloor.HasValue ? $"{fixedFloor.Value:0.##}h / day" : "Derived from scheduled blocks",
            summaryWorkHours: workHoursStart.HasValue && workHoursEnd.HasValue
                ? $"{workHoursStart:HH:mm} – {workHoursEnd:HH:mm}"
                : "No constraint",
            summaryRestDays: restDays.Count > 0 ? string.Join(", ", restDays) : "None",
            summaryRedemptionPlacement: redemptionPlacementPolicy == RedemptionPlacementPolicy.CurrentDayOnly
                ? "Current day only"
                : "Anywhere in the contract",
            onReadyToSchedule: _onReadyToSchedule,
            onBack: NavigateToRules);
    }

    // Cosmetic-only display mapping — keeps the Review step's wording aligned with the
    // renamed Simple presets (StrictnessRule itself is unchanged pending backend follow-up).
    private static string FriendlyStrictnessLabel(StrictnessRule rule) => rule switch
    {
        StrictnessRule.Locked => "Lockdown",
        StrictnessRule.DailyFlexibility => "Daily Flex",
        StrictnessRule.PeriodTimeBudget => "Period Study",
        _ => rule.ToString()
    };
}
