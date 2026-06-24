using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Application.Queries.GetFocusBlockSchedule;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Ritual;

public partial class RitualShellViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> _profilesQuery;
    private readonly ICommandHandler<StartSessionCommand, StartSessionResult> _startSession;
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

    private Action _currentBackAction = () => { };

    public bool IsDot0Active => StepIndex == 0;
    public bool IsDot1Active => StepIndex == 1;
    public bool IsDot2Active => StepIndex == 2;
    public bool IsDot3Active => StepIndex == 3;

    private readonly Guid? _originatingBlockId;
    private readonly BlockProfileDto? _lockedProfile;
    private readonly FocusBlockSchedule? _lockedSchedule;
    private readonly SessionOrigin _mode;

    private string _goal = string.Empty;
    private Guid? _selectedProfileId;

    private ZetsuPageViewModel? _zetsuVm;
    private RenPageViewModel? _renVm;

    public RitualShellViewModel(
        IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> profilesQuery,
        ICommandHandler<StartSessionCommand, StartSessionResult> startSession,
        ISystemClock clock,
        Guid? originatingBlockId,
        BlockProfileDto? lockedProfile,
        FocusBlockSchedule? lockedSchedule,
        Action onComplete,
        Action onCancel)
    {
        _profilesQuery = profilesQuery;
        _startSession = startSession;
        _clock = clock;
        _originatingBlockId = originatingBlockId;
        _lockedProfile = lockedProfile;
        _lockedSchedule = lockedSchedule;
        _mode = lockedProfile is not null ? SessionOrigin.Planned : SessionOrigin.Spontaneous;
        _onComplete = onComplete;
        _onCancel = onCancel;

        NavigateToTen();
    }

    [RelayCommand]
    private void GoBack() => _currentBackAction();

    private void NavigateToTen()
    {
        StepIndex = 0;
        _currentBackAction = _onCancel;
        _zetsuVm = null;
        CurrentStep = new TenPageViewModel(
            onContinue: NavigateToZetsu,
            onBack: _onCancel);
    }

    private void NavigateToZetsu()
    {
        StepIndex = 1;
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
            profilesQuery: _mode == SessionOrigin.Spontaneous ? _profilesQuery : null,
            mode: _mode,
            lockedProfile: _lockedProfile,
            onContinue: NavigateToHatsu,
            onBack: NavigateToZetsu);
        _currentBackAction = NavigateToZetsu;
        CurrentStep = _renVm;
    }

    private void NavigateToHatsu()
    {
        if (_renVm is not null && _mode == SessionOrigin.Spontaneous)
            _selectedProfileId = _renVm.SelectedProfile?.Id;
        else if (_mode == SessionOrigin.Planned)
            _selectedProfileId = _lockedProfile?.Id;

        StepIndex = 3;
        _currentBackAction = NavigateToRen;
        CurrentStep = new HatsuPageViewModel(
            startSession: _startSession,
            clock: _clock,
            getData: () => new HatsuData
            {
                Goal = _goal,
                ProfileId = _selectedProfileId,
                OriginatingBlockId = _originatingBlockId,
                AdditionalBlockedSites = _renVm?.AdditionalBlockedSites ?? (IReadOnlyList<string>)[],
                AdditionalBlockedApps = _renVm?.AdditionalBlockedApps ?? (IReadOnlyList<string>)[]
            },
            onSessionStarted: _onComplete,
            onBack: NavigateToRen,
            plannedSchedule: _mode == SessionOrigin.Planned ? _lockedSchedule : null);
    }
}
