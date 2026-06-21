using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
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
    private readonly ICommandHandler<StartSessionCommand, StartSessionResult> _startSession;
    private readonly ICommandHandler<EndSessionCommand, EndSessionResult> _endSession;
    private readonly ICommandHandler<StartBreakCommand, StartBreakResult> _startBreak;
    private readonly ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> _resumeFromBreak;
    private readonly ICommandHandler<SaveReflectionCommand, SaveReflectionResult> _saveReflection;
    private readonly ISessionStateService _sessionState;
    private readonly IActiveSessionMonitor _monitor;
    private readonly IPlannerClockService _plannerClock;

    [ObservableProperty] private ViewModelBase _currentPage = null!;
    [ObservableProperty] private bool _isRitualActive;
    [ObservableProperty] private RitualShellViewModel? _ritualViewModel;
    [ObservableProperty] private bool _isPostSessionActive;
    [ObservableProperty] private PostSessionViewModel? _postSessionViewModel;

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public ShellViewModel(
       IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>> profilesQuery,
       ICommandHandler<StartSessionCommand, StartSessionResult> startSession,
       ICommandHandler<EndSessionCommand, EndSessionResult> endSession,
       ICommandHandler<StartBreakCommand, StartBreakResult> startBreak,
       ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> resumeFromBreak,
       ICommandHandler<SaveReflectionCommand, SaveReflectionResult> saveReflection,
       ISessionStateService sessionState,
       IActiveSessionMonitor monitor,
       IPlannerClockService plannerClock,
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
        _startSession = startSession;
        _endSession = endSession;
        _startBreak = startBreak;
        _resumeFromBreak = resumeFromBreak;
        _saveReflection = saveReflection;
        _sessionState = sessionState;
        _monitor = monitor;
        _plannerClock = plannerClock;

        _sessionState.RitualRequirementChanged += (s, required) =>
        {
            if (required && !IsRitualActive)
            {
                // Dispatch to UI thread to handle visual changes
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
        NavItems.Add(new NavItemViewModel("Planner", "◫", new PlannerPageViewModel(profilesQuery, getBlocksForDay, createPlannedBlock, updatePlannedBlock, deletePlannedBlock, _plannerClock), Select));
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

    private void StartRitual()
    {
        RitualViewModel = new RitualShellViewModel(
            profilesQuery: _profilesQuery,
            startSession: _startSession,
            onComplete: ExitRitual,
            onCancel: ExitRitual);
        IsRitualActive = true;
    }

    private void ExitRitual()
    {
        IsRitualActive = false;
        RitualViewModel = null;
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