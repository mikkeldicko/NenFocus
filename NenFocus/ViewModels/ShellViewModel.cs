using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;
using NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Application.UseCases.Sessions.StartBreak;
using NenFocus.Application.UseCases.Sessions.StartSession;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using NenFocus.ViewModels.Pages;
using NenFocus.ViewModels.Ritual;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace NenFocus.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> _profilesQuery;
    private readonly IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?> _getFocusBlockRitualContext;
    private readonly ICommandHandler<StartSessionCommand, StartSessionResult> _startSession;
    private readonly ICommandHandler<EndSessionCommand, EndSessionResult> _endSession;
    private readonly ICommandHandler<StartBreakCommand, StartBreakResult> _startBreak;
    private readonly ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> _resumeFromBreak;
    private readonly ICommandHandler<SaveReflectionCommand, SaveReflectionResult> _saveReflection;
    private readonly ISessionStateService _sessionState;
    private readonly IActiveSessionMonitor _monitor;
    private readonly IPlannerClockService _plannerClock;
    private readonly ISystemClock _clock;

    [ObservableProperty] private ViewModelBase _currentPage = null!;
    [ObservableProperty] private bool _isRitualActive;
    [ObservableProperty] private RitualShellViewModel? _ritualViewModel;
    [ObservableProperty] private bool _isPostSessionActive;
    [ObservableProperty] private PostSessionViewModel? _postSessionViewModel;

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public ShellViewModel(
       IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> profilesQuery,
       IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?> getFocusBlockRitualContext,
       ICommandHandler<StartSessionCommand, StartSessionResult> startSession,
       ICommandHandler<EndSessionCommand, EndSessionResult> endSession,
       ICommandHandler<StartBreakCommand, StartBreakResult> startBreak,
       ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> resumeFromBreak,
       ICommandHandler<SaveReflectionCommand, SaveReflectionResult> saveReflection,
       ISessionStateService sessionState,
       IActiveSessionMonitor monitor,
       IPlannerClockService plannerClock,
       ISystemClock clock,
       ICommandHandler<CreateBlockProfileCommand, CreateBlockProfileResult> createProfile,
       ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult> updateProfile,
       ICommandHandler<DeleteBlockProfileCommand, DeleteBlockProfileResult> deleteProfile,
       ICommandHandler<DuplicateBlockProfileCommand, DuplicateBlockProfileResult> duplicateProfile,
       IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
       ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createPlannedBlock,
       ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updatePlannedBlock,
       ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deletePlannedBlock)
    {
        _profilesQuery = profilesQuery;
        _getFocusBlockRitualContext = getFocusBlockRitualContext;
        _startSession = startSession;
        _endSession = endSession;
        _startBreak = startBreak;
        _resumeFromBreak = resumeFromBreak;
        _saveReflection = saveReflection;
        _sessionState = sessionState;
        _monitor = monitor;
        _plannerClock = plannerClock;
        _clock = clock;

        _sessionState.RitualRequirementChanged += (s, required) =>
        {
            if (required && !IsRitualActive)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StartRitual();
                    BringToForeground();
                });
            }
        };

        void Select(NavItemViewModel selected)
        {
            foreach (var item in NavItems) item.IsSelected = false;
            selected.IsSelected = true;
            CurrentPage = selected.PageViewModel;
        }

        NavItems.Add(new NavItemViewModel("Main", "⊹", new MainPageViewModel(StartRitual, ShowPostSession, _sessionState, _monitor, _endSession, _startBreak, _resumeFromBreak, _plannerClock), Select));
        NavItems.Add(new NavItemViewModel("Block Profiles", "◻", new BlockProfilePageViewModel(profilesQuery, createProfile, updateProfile, deleteProfile, duplicateProfile), Select));
        NavItems.Add(new NavItemViewModel("Planner", "◫", new PlannerPageViewModel(profilesQuery, getBlocksForDay, createPlannedBlock, updatePlannedBlock, deletePlannedBlock, _plannerClock, _sessionState), Select));
        NavItems.Add(new NavItemViewModel("Insights", "◈", new InsightsPageViewModel(), Select));
        NavItems.Add(new NavItemViewModel("Settings", "◎", new SettingsPageViewModel(), Select));

        NavItems[0].IsSelected = true;
        CurrentPage = NavItems[0].PageViewModel;
    }

    private void BringToForeground()
    {
        // Use 'global::Avalonia' to tell the compiler NOT to look in 'NenFocus.Application'
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();

                // Briefly toggle Topmost to "steal" focus from other background apps
                window.Topmost = true;
                window.Topmost = false;
            }
        }
    }

    private async void StartRitual()
    {
        BlockProfileDto? lockedProfile = null;
        FocusBlockSchedule? lockedSchedule = null;

        if (_sessionState.PendingFocusBlockId.HasValue)
        {
            var blockId = _sessionState.PendingFocusBlockId.Value;
            var context = await _getFocusBlockRitualContext.HandleAsync(
                new GetFocusBlockRitualContextQuery { FocusBlockId = blockId });
            lockedProfile = context?.Profile;
            lockedSchedule = context?.Schedule;
        }

        bool isPlanned = lockedProfile is not null;

        RitualViewModel = new RitualShellViewModel(
            profilesQuery: _profilesQuery,
            startSession: _startSession,
            clock: _clock,
            originatingBlockId: _sessionState.PendingFocusBlockId,
            lockedProfile: lockedProfile,
            lockedSchedule: lockedSchedule,
            onComplete: ExitRitualAndGoToMain,
            onCancel: isPlanned ? () => { } : ExitRitual);
        IsRitualActive = true;
    }

    private void ExitRitual()
    {
        IsRitualActive = false;
        RitualViewModel = null;
    }

    private void ExitRitualAndGoToMain()
    {
        IsRitualActive = false;
        RitualViewModel = null;

        // Always land on the Main page so the session timer is immediately visible.
        foreach (var item in NavItems) item.IsSelected = false;
        NavItems[0].IsSelected = true;
        CurrentPage = NavItems[0].PageViewModel;
    }

    private void ShowPostSession(PostSessionDto data)
    {
        PostSessionViewModel = new PostSessionViewModel(data, _saveReflection, ExitPostSession);
        IsPostSessionActive = true;
    }

    private void ExitPostSession()
    {
        IsPostSessionActive = false;
        PostSessionViewModel = null;
    }
}