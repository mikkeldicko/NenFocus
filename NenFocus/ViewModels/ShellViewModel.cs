using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Application.UseCases.Sessions.StartBreak;
using NenFocus.Application.UseCases.Sessions.StartSession;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using NenFocus.ViewModels.Contracts;
using NenFocus.ViewModels.Pages;
using NenFocus.ViewModels.Ritual;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace NenFocus.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> _presetsQuery;
    private readonly IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?> _getFocusBlockRitualContext;
    private readonly ICommandHandler<StartSessionCommand, StartSessionResult> _startSession;
    private readonly ICommandHandler<EndSessionCommand, EndSessionResult> _endSession;
    private readonly ICommandHandler<StartBreakCommand, StartBreakResult> _startBreak;
    private readonly ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> _resumeFromBreak;
    private readonly ICommandHandler<SaveReflectionCommand, SaveReflectionResult> _saveReflection;
    private readonly ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> _markFocusBlockMissed;
    private readonly ISessionStateService _sessionState;
    private readonly IActiveSessionMonitor _monitor;
    private readonly IPlannerClockService _plannerClock;
    private readonly ISystemClock _clock;

    private readonly Queue<PendingOverlay> _pendingOverlays = new();
    private bool _suppressTriage;

    private abstract record PendingOverlay;
    private sealed record PendingMissedSession(PostSessionDto PostSession) : PendingOverlay;
    private sealed record PendingRitual(Guid FocusBlockId) : PendingOverlay;

    [ObservableProperty] private ViewModelBase _currentPage = null!;
    [ObservableProperty] private bool _isRitualActive;
    [ObservableProperty] private RitualShellViewModel? _ritualViewModel;
    [ObservableProperty] private bool _isPostSessionActive;
    [ObservableProperty] private PostSessionViewModel? _postSessionViewModel;
    [ObservableProperty] private bool _isTriageActive;
    [ObservableProperty] private MissedSessionsTriageViewModel? _triageViewModel;
    [ObservableProperty] private bool _isContractWizardActive;
    [ObservableProperty] private ContractWizardViewModel? _contractWizardViewModel;

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public ShellViewModel(
       IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> presetsQuery,
       IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?> getFocusBlockRitualContext,
       ICommandHandler<StartSessionCommand, StartSessionResult> startSession,
       ICommandHandler<EndSessionCommand, EndSessionResult> endSession,
       ICommandHandler<StartBreakCommand, StartBreakResult> startBreak,
       ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> resumeFromBreak,
       ICommandHandler<SaveReflectionCommand, SaveReflectionResult> saveReflection,
       ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> markFocusBlockMissed,
       ISessionStateService sessionState,
       IActiveSessionMonitor monitor,
       IPlannerClockService plannerClock,
       ISystemClock clock,
       ICommandHandler<CreateBlockPresetCommand, CreateBlockPresetResult> createPreset,
       ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult> updatePreset,
       ICommandHandler<DeleteBlockPresetCommand, DeleteBlockPresetResult> deletePreset,
       ICommandHandler<DuplicateBlockPresetCommand, DuplicateBlockPresetResult> duplicatePreset,
       IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
       ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createPlannedBlock,
       ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updatePlannedBlock,
       ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deletePlannedBlock,
       ICommandHandler<SealNenContractCommand, SealNenContractResult> sealContract,
       IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours,
       IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> getContractPresets,
       ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> createContractPreset)
    {
        _presetsQuery = presetsQuery;
        _getFocusBlockRitualContext = getFocusBlockRitualContext;
        _startSession = startSession;
        _endSession = endSession;
        _startBreak = startBreak;
        _resumeFromBreak = resumeFromBreak;
        _saveReflection = saveReflection;
        _markFocusBlockMissed = markFocusBlockMissed;
        _sessionState = sessionState;
        _monitor = monitor;
        _plannerClock = plannerClock;
        _clock = clock;

        _sessionState.RitualRequirementChanged += (s, change) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnRitualRequirementChanged(change));

        _sessionState.MissedSessionsDiscovered += (s, sessions) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnMissedSessionsDiscovered(sessions));

        void Select(NavItemViewModel selected)
        {
            foreach (var item in NavItems) item.IsSelected = false;
            selected.IsSelected = true;
            CurrentPage = selected.PageViewModel;
        }

        NavItems.Add(new NavItemViewModel("Main", "⊹", new MainPageViewModel(() => StartRitual(), ShowPostSession, _sessionState, _monitor, _endSession, _startBreak, _resumeFromBreak, _plannerClock), Select));
        NavItems.Add(new NavItemViewModel("Presets", "◻", new BlockPresetPageViewModel(presetsQuery, createPreset, updatePreset, deletePreset, duplicatePreset), Select));
        var plannerViewModel = new PlannerPageViewModel(presetsQuery, getBlocksForDay, createPlannedBlock, updatePlannedBlock, deletePlannedBlock, sealContract, computePlannedFocusHours, getContractPresets, createContractPreset, _plannerClock, _sessionState);
        plannerViewModel.ContractWizardOpened += OnContractWizardOpened;
        plannerViewModel.ContractWizardClosed += OnContractWizardClosed;
        NavItems.Add(new NavItemViewModel("Planner", "◫", plannerViewModel, Select));
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

    private async void StartRitual(Guid? focusBlockId = null)
    {
        BlockPresetDto? lockedPreset = null;
        FocusBlockSchedule? lockedSchedule = null;

        if (focusBlockId.HasValue)
        {
            var context = await _getFocusBlockRitualContext.HandleAsync(
                new GetFocusBlockRitualContextQuery { FocusBlockId = focusBlockId.Value });
            lockedPreset = context?.Preset;
            lockedSchedule = context?.Schedule;
        }

        bool isPlanned = lockedPreset is not null;

        RitualViewModel = new RitualShellViewModel(
            presetsQuery: _presetsQuery,
            startSession: _startSession,
            clock: _clock,
            originatingBlockId: focusBlockId,
            lockedPreset: lockedPreset,
            lockedSchedule: lockedSchedule,
            onComplete: ExitRitualAndGoToMain,
            onCancel: isPlanned ? () => { } : ExitRitual);
        IsRitualActive = true;
    }

    private void OnRitualRequirementChanged(RitualRequirementChange change)
    {
        if (change.Required)
        {
            if (change.HandoffFromFocusBlockId.HasValue)
                _ = HandleRitualHandoff(change.HandoffFromFocusBlockId.Value, change.FocusBlockId);
            else
                EnqueueOrStartRitual(change.FocusBlockId);
            return;
        }

        if (IsRitualActive && change.FocusBlockId.HasValue)
            _ = HandleLiveRitualRelease(change.FocusBlockId.Value);
    }

    private void EnqueueOrStartRitual(Guid? focusBlockId)
    {
        if (IsRitualActive) return;

        if (IsPostSessionActive || IsTriageActive || IsContractWizardActive)
        {
            if (focusBlockId.HasValue)
                _pendingOverlays.Enqueue(new PendingRitual(focusBlockId.Value));
            return;
        }

        StartRitual(focusBlockId);
        BringToForeground();
    }

    private async Task HandleLiveRitualRelease(Guid focusBlockId)
    {
        // The block's window closed while its ritual was on screen — the ritual is
        // dead and can't wait behind anything else in the queue, so close it now.
        CloseRitualOverlay();

        var postSession = await TryMarkBlockMissed(focusBlockId);
        if (postSession is not null)
            _pendingOverlays.Enqueue(new PendingMissedSession(postSession));

        TryDrainQueue();
    }

    private async Task HandleRitualHandoff(Guid previousFocusBlockId, Guid? nextFocusBlockId)
    {
        // Back-to-back scheduled blocks: the previous block's window closed and the
        // next one became active in the same heartbeat tick, with no intervening
        // false transition. The previous block never gets its own release, so resolve
        // it here — same sequence as a live release, just triggered differently.
        if (IsRitualActive)
            CloseRitualOverlay();

        var postSession = await TryMarkBlockMissed(previousFocusBlockId);
        if (postSession is not null)
            _pendingOverlays.Enqueue(new PendingMissedSession(postSession));

        // Enqueued behind the missed session above (not started directly) so the
        // user deals with the miss before the next block's ritual takes over — the
        // single TryDrainQueue() call below surfaces them in that order.
        if (nextFocusBlockId.HasValue)
            _pendingOverlays.Enqueue(new PendingRitual(nextFocusBlockId.Value));

        TryDrainQueue();
    }

    private async Task<PostSessionDto?> TryMarkBlockMissed(Guid focusBlockId)
    {
        try
        {
            var result = await _markFocusBlockMissed.HandleAsync(
                new MarkFocusBlockMissedCommand { FocusBlockId = focusBlockId });

            return result.Success ? result.PostSession : null;
        }
        catch
        {
            // The block itself is untouched by a failed attempt — the next cold-start
            // sweep or heartbeat tick will pick it up again. What must not happen is
            // the shell getting stuck with nothing draining the queue, so every caller
            // still proceeds to TryDrainQueue() after this returns.
            return null;
        }
    }

    private void OnMissedSessionsDiscovered(IReadOnlyList<PostSessionDto> sessions)
    {
        foreach (var session in sessions)
            _pendingOverlays.Enqueue(new PendingMissedSession(session));

        TryDrainQueue();
    }

    private void TryDrainQueue()
    {
        if (IsRitualActive || IsPostSessionActive || IsTriageActive || IsContractWizardActive) return;

        if (_pendingOverlays.Count == 0)
        {
            _suppressTriage = false;
            GoToMain();
            return;
        }

        if (!_suppressTriage)
        {
            var missedCount = _pendingOverlays.OfType<PendingMissedSession>().Count();
            if (missedCount > 3)
            {
                ShowTriage(missedCount);
                return;
            }
        }

        switch (_pendingOverlays.Dequeue())
        {
            case PendingMissedSession missed:
                ShowPostSession(missed.PostSession);
                break;
            case PendingRitual ritual:
                _ = HandlePendingRitual(ritual.FocusBlockId);
                break;
        }
    }

    private async Task HandlePendingRitual(Guid focusBlockId)
    {
        // Time may have passed while this sat behind another overlay — if the block's
        // window already closed, there's nothing left to ritual into, so resolve it
        // the same way a live release would instead of opening a dead Ritual screen.
        var context = await _getFocusBlockRitualContext.HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = focusBlockId });

        if (context is not null && context.Schedule.End <= _clock.UtcNow)
        {
            var postSession = await TryMarkBlockMissed(focusBlockId);
            if (postSession is not null)
                _pendingOverlays.Enqueue(new PendingMissedSession(postSession));

            TryDrainQueue();
            return;
        }

        StartRitual(focusBlockId);
        BringToForeground();
    }

    private void ShowTriage(int count)
    {
        TriageViewModel = new MissedSessionsTriageViewModel(count, SkipAllMissedSessions, ReviewEachMissedSession);
        IsTriageActive = true;
    }

    private void SkipAllMissedSessions()
    {
        var kept = _pendingOverlays.Where(o => o is not PendingMissedSession).ToList();
        _pendingOverlays.Clear();
        foreach (var item in kept) _pendingOverlays.Enqueue(item);

        HideTriage();
        TryDrainQueue();
    }

    private void ReviewEachMissedSession()
    {
        _suppressTriage = true;
        HideTriage();
        TryDrainQueue();
    }

    private void HideTriage()
    {
        IsTriageActive = false;
        TriageViewModel = null;
    }

    private void OnContractWizardOpened(ContractWizardViewModel vm)
    {
        ContractWizardViewModel = vm;
        IsContractWizardActive = true;
    }

    private void OnContractWizardClosed()
    {
        IsContractWizardActive = false;
        ContractWizardViewModel = null;
        TryDrainQueue();
    }

    private void GoToMain()
    {
        foreach (var item in NavItems) item.IsSelected = false;
        NavItems[0].IsSelected = true;
        CurrentPage = NavItems[0].PageViewModel;
    }

    private void CloseRitualOverlay()
    {
        IsRitualActive = false;
        RitualViewModel = null;
    }

    private void ExitRitual()
    {
        CloseRitualOverlay();
        TryDrainQueue();
    }

    private void ExitRitualAndGoToMain()
    {
        CloseRitualOverlay();
        TryDrainQueue();
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
        TryDrainQueue();
    }
}