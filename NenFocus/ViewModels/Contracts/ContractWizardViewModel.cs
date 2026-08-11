using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
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
    private readonly IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> _getPresets;
    private readonly ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> _createPreset;
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

    private ContractBasicsStepViewModel? _basicsVm;
    private ContractModeStepViewModel? _modeVm;
    private ContractRulesStepViewModel? _rulesVm;

    public ContractWizardViewModel(
        IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> getPresets,
        ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> createPreset,
        DateOnly initialStartDate,
        Action<NenContractDraftRules> onReadyToSchedule,
        Action onCancel)
    {
        _getPresets = getPresets;
        _createPreset = createPreset;
        _startDate = initialStartDate;
        _endDate = initialStartDate.AddDays(6);
        _onReadyToSchedule = onReadyToSchedule;
        _onCancel = onCancel;

        NavigateToBasics();
    }

    [RelayCommand] private void GoBack() => _currentBackAction();

    private void NavigateToBasics()
    {
        StepIndex = 0;
        IsBackVisible = true;
        _currentBackAction = _onCancel;
        _basicsVm = new ContractBasicsStepViewModel(
            initialName: _name,
            initialGoal: _goal,
            initialStartDate: new DateTimeOffset(_startDate.ToDateTime(TimeOnly.MinValue)),
            initialEndDate: new DateTimeOffset(_endDate.ToDateTime(TimeOnly.MinValue)),
            onContinue: NavigateToMode,
            onBack: _onCancel);
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
            getPresets: _isAdvanced ? _getPresets : null,
            onContinue: NavigateToReview,
            onBack: NavigateToMode);
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
        var modeLabel = rules?.ResolvedSummaryLabel ?? "Simple";

        CreateContractPresetCommand? presetToSave = rules is { IsAdvanced: true, SaveAsPreset: true }
            ? new CreateContractPresetCommand
            {
                Name = rules.PresetNameToSave,
                StrictnessRule = strictness,
                DailyFloorHours = fixedFloor ?? 0,
                WorkHoursStart = workHoursStart,
                WorkHoursEnd = workHoursEnd,
                AllowCancellation = allowCancellation
            }
            : null;

        StepIndex = 3;
        _currentBackAction = NavigateToRules;
        CurrentStep = new ContractReviewStepViewModel(
            createPreset: _createPreset,
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
                ContractPresetId = contractPresetId
            },
            presetToSave: presetToSave,
            summaryName: _name,
            summaryDateRange: $"{_startDate:MMM d, yyyy} – {_endDate:MMM d, yyyy}",
            summaryMode: modeLabel,
            summaryStrictness: FriendlyStrictnessLabel(strictness),
            summaryFloor: fixedFloor.HasValue ? $"{fixedFloor.Value:0.##}h / day" : "Derived from scheduled blocks",
            summaryWorkHours: workHoursStart.HasValue && workHoursEnd.HasValue
                ? $"{workHoursStart:HH:mm} – {workHoursEnd:HH:mm}"
                : "No constraint",
            summaryRestDays: restDays.Count > 0 ? string.Join(", ", restDays) : "None",
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
