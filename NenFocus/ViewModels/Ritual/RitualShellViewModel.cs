using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Ritual;

public partial class RitualShellViewModel : ViewModelBase
{
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly ISystemClock _clock;
    private readonly Action _onComplete;
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

    // Late-to-block modal — Ritual-level pre-condition, not step-specific, so it lives
    // here rather than on TenPageViewModel: if steps are reordered or added later, the
    // modal stays in the right place regardless. Raw values only (DateTime/TimeSpan,
    // already computed by GetFocusBlockRitualContextQueryHandler) — no math or string
    // formatting here, per MODULE_STANDARDS.md's ViewModel Forbidden Zone; the View
    // formats BlockStart/BlockEnd/LateBy through converters.
    [ObservableProperty]
    private bool _isLateToBlock;

    [ObservableProperty]
    private DateTime _lateBlockStart;

    [ObservableProperty]
    private DateTime _lateBlockEnd;

    [ObservableProperty]
    private TimeSpan _lateBy;

    private Action _currentBackAction = () => { };

    public bool IsDot0Active => StepIndex == 0;
    public bool IsDot1Active => StepIndex == 1;
    public bool IsDot2Active => StepIndex == 2;
    public bool IsDot3Active => StepIndex == 3;

    private readonly Guid? _originatingBlockId;
    private readonly BlockPresetDto? _lockedPreset;
    private readonly FocusBlockSchedule? _lockedSchedule;
    private readonly SessionOrigin _mode;

    private string _goal = string.Empty;
    private Guid? _selectedPresetId;

    private ZetsuPageViewModel? _zetsuVm;
    private RenPageViewModel? _renVm;

    public RitualShellViewModel(
        IQueryDispatcher queryDispatcher,
        ICommandDispatcher commandDispatcher,
        ISystemClock clock,
        Guid? originatingBlockId,
        BlockPresetDto? lockedPreset,
        FocusBlockSchedule? lockedSchedule,
        Action onComplete,
        Action onCancel,
        bool isLateToBlock = false,
        TimeSpan lateBy = default)
    {
        _queryDispatcher = queryDispatcher;
        _commandDispatcher = commandDispatcher;
        _clock = clock;
        _originatingBlockId = originatingBlockId;
        _lockedPreset = lockedPreset;
        _lockedSchedule = lockedSchedule;
        _mode = lockedPreset is not null ? SessionOrigin.Planned : SessionOrigin.Spontaneous;
        _onComplete = onComplete;
        _onCancel = onCancel;

        // Only ever true for a Planned ritual (lockedSchedule is null for an impromptu
        // session, so there's nothing to be "late" against) — guard here too rather
        // than trusting the caller never to pass isLateToBlock=true without a schedule.
        if (isLateToBlock && lockedSchedule is not null)
        {
            IsLateToBlock = true;
            LateBlockStart = lockedSchedule.Start.ToLocalTime();
            LateBlockEnd = lockedSchedule.End.ToLocalTime();
            LateBy = lateBy;
        }

        NavigateToTen();
    }

    [RelayCommand]
    private void GoBack() => _currentBackAction();

    [RelayCommand]
    private void DismissLateNotice() => IsLateToBlock = false;

    private void NavigateToTen()
    {
        StepIndex = 0;
        _currentBackAction = _onCancel;
        // Planned rituals are mandatory — hide the back/cancel button on Ten.
        IsBackVisible = _mode != SessionOrigin.Planned;
        _zetsuVm = null;
        CurrentStep = new TenPageViewModel(
            onContinue: NavigateToZetsu,
            onBack: _onCancel);
    }

    private void NavigateToZetsu()
    {
        StepIndex = 1;
        IsBackVisible = true;
        _zetsuVm = new ZetsuPageViewModel(
            onContinue: NavigateToRen,
            onBack: NavigateToTen);
        _currentBackAction = NavigateToTen;
        CurrentStep = _zetsuVm;
    }

    private void NavigateToRen()
    {
        if (_zetsuVm is not null)
            _goal = _zetsuVm.Goal;

        StepIndex = 2;
        _renVm = new RenPageViewModel(
            queryDispatcher: _queryDispatcher,
            mode: _mode,
            lockedPreset: _lockedPreset,
            onContinue: NavigateToHatsu,
            onBack: NavigateToZetsu);
        _currentBackAction = NavigateToZetsu;
        CurrentStep = _renVm;
    }

    private void NavigateToHatsu()
    {
        if (_renVm is not null && _mode == SessionOrigin.Spontaneous)
            _selectedPresetId = _renVm.SelectedPreset?.Id;
        else if (_mode == SessionOrigin.Planned)
            _selectedPresetId = _lockedPreset?.Id;

        StepIndex = 3;
        _currentBackAction = NavigateToRen;
        CurrentStep = new HatsuPageViewModel(
            commandDispatcher: _commandDispatcher,
            clock: _clock,
            getData: () => new HatsuData
            {
                Goal = _goal,
                PresetId = _selectedPresetId,
                OriginatingBlockId = _originatingBlockId,
                AdditionalBlockedSites = _renVm?.AdditionalBlockedSites ?? (IReadOnlyList<string>)[],
                AdditionalBlockedApps = _renVm?.AdditionalBlockedApps ?? (IReadOnlyList<string>)[]
            },
            onSessionStarted: _onComplete,
            onBack: NavigateToRen,
            plannedSchedule: _mode == SessionOrigin.Planned ? _lockedSchedule : null);
    }
}
