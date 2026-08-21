using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.Queries.GetActiveNenContract;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.Queries.GetScheduledNenContracts;
using NenFocus.Application.Queries.GetSessionSnapshotForBlock;
using NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;
using NenFocus.Application.Queries.GetUnacknowledgedPostSessions;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;
using NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;
using NenFocus.Application.UseCases.Sessions.EndSession;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Application.UseCases.Sessions.StartBreak;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using NenFocus.ViewModels.Contracts;
using NenFocus.ViewModels.DevTools;
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
    private readonly ICommandHandler<AcknowledgePostSessionCommand, AcknowledgePostSessionResult> _acknowledgePostSession;
    private readonly ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> _markFocusBlockMissed;
    private readonly IQueryHandler<GetUnacknowledgedPostSessionsQuery, IReadOnlyList<PostSessionDto>> _getUnacknowledgedPostSessions;
    private readonly ISessionStateService _sessionState;
    private readonly IActiveSessionMonitor _monitor;
    private readonly IPlannerClockService _plannerClock;
    private readonly ISystemClock _clock;

    private readonly Queue<PendingOverlay> _pendingOverlays = new();
    private bool _suppressTriage;

    private abstract record PendingOverlay;
    private sealed record PendingPostSession(PostSessionDto PostSession) : PendingOverlay;
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

    public DebugClockViewModel DebugClockViewModel { get; }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public ShellViewModel(
       IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> presetsQuery,
       IQueryHandler<GetFocusBlockRitualContextQuery, FocusBlockRitualContext?> getFocusBlockRitualContext,
       ICommandHandler<StartSessionCommand, StartSessionResult> startSession,
       ICommandHandler<EndSessionCommand, EndSessionResult> endSession,
       ICommandHandler<StartBreakCommand, StartBreakResult> startBreak,
       ICommandHandler<ResumeFromBreakCommand, ResumeFromBreakResult> resumeFromBreak,
       ICommandHandler<SaveReflectionCommand, SaveReflectionResult> saveReflection,
       ICommandHandler<AcknowledgePostSessionCommand, AcknowledgePostSessionResult> acknowledgePostSession,
       ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult> markFocusBlockMissed,
       IQueryHandler<GetUnacknowledgedPostSessionsQuery, IReadOnlyList<PostSessionDto>> getUnacknowledgedPostSessions,
       ISessionStateService sessionState,
       IActiveSessionMonitor monitor,
       IPlannerClockService plannerClock,
       ISystemClock clock,
       IDebugClockController debugClockController,
       ICommandHandler<CreateBlockPresetCommand, CreateBlockPresetResult> createPreset,
       ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult> updatePreset,
       ICommandHandler<DeleteBlockPresetCommand, DeleteBlockPresetResult> deletePreset,
       ICommandHandler<DuplicateBlockPresetCommand, DuplicateBlockPresetResult> duplicatePreset,
       IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
       ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createPlannedBlock,
       ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updatePlannedBlock,
       ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deletePlannedBlock,
       ICommandHandler<SplitPlannedBlockCommand, SplitPlannedBlockResult> splitPlannedBlock,
       ICommandHandler<SealNenContractCommand, SealNenContractResult> sealContract,
       ICommandHandler<BreakNenContractCommand, BreakNenContractResult> breakContract,
       IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours,
       IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> getContractPresets,
       ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult> createContractPreset,
       ICommandHandler<UpdateContractPresetCommand, UpdateContractPresetResult> updateContractPreset,
       ICommandHandler<DeleteContractPresetCommand, DeleteContractPresetResult> deleteContractPreset,
       IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?> getActiveContract,
       IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution,
       IQueryHandler<GetSuggestedRedemptionPlacementsQuery, IReadOnlyList<SuggestedRedemptionPlacementDto>> getSuggestedRedemptionPlacements,
       IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap,
       IQueryHandler<GetScheduledNenContractsQuery, IReadOnlyList<ScheduledNenContractDto>> getScheduledContracts,
       IQueryHandler<GetSessionSnapshotForBlockQuery, SessionSnapshotDto?> getSessionSnapshot,
       IQueryHandler<GetNenContractForEditQuery, NenContractEditDto?> getContractForEdit,
       ICommandHandler<EditNenContractCommand, EditNenContractResult> editContract,
       ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult> deleteContract)
    {
        _presetsQuery = presetsQuery;
        _getFocusBlockRitualContext = getFocusBlockRitualContext;
        _startSession = startSession;
        _endSession = endSession;
        _startBreak = startBreak;
        _resumeFromBreak = resumeFromBreak;
        _saveReflection = saveReflection;
        _acknowledgePostSession = acknowledgePostSession;
        _markFocusBlockMissed = markFocusBlockMissed;
        _getUnacknowledgedPostSessions = getUnacknowledgedPostSessions;
        _sessionState = sessionState;
        _monitor = monitor;
        _plannerClock = plannerClock;
        _clock = clock;
        DebugClockViewModel = new DebugClockViewModel(clock, debugClockController);

        _sessionState.RitualRequirementChanged += (s, change) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnRitualRequirementChanged(change));
        _sessionState.PostSessionsReady += (s, postSessions) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnPostSessionsReady(postSessions));

        void Select(NavItemViewModel selected)
        {
            foreach (var item in NavItems) item.IsSelected = false;
            selected.IsSelected = true;
            CurrentPage = selected.PageViewModel;
        }

        NavItems.Add(new NavItemViewModel("Main", "⊹", new MainPageViewModel(() => StartRitual(), ShowPostSession, _sessionState, _monitor, _endSession, _startBreak, _resumeFromBreak, _plannerClock), Select));
        var presetsPageViewModel = new PresetsPageViewModel(
            new BlockPresetPageViewModel(presetsQuery, createPreset, updatePreset, deletePreset, duplicatePreset),
            new ContractPresetPageViewModel(getContractPresets, createContractPreset, updateContractPreset, deleteContractPreset));
        NavItems.Add(new NavItemViewModel("Presets", "◻", presetsPageViewModel, Select));
        var plannerViewModel = new PlannerPageViewModel(presetsQuery, getBlocksForDay, createPlannedBlock, updatePlannedBlock, deletePlannedBlock, splitPlannedBlock, sealContract, breakContract, computePlannedFocusHours, getContractPresets, getActiveContract, computeRedemptionAttribution, getSuggestedRedemptionPlacements, checkOverlap, getScheduledContracts, getSessionSnapshot, getContractForEdit, editContract, deleteContract, _plannerClock, _sessionState);
        plannerViewModel.ContractWizardOpened += OnContractWizardOpened;
        plannerViewModel.ContractWizardClosed += OnContractWizardClosed;
        NavItems.Add(new NavItemViewModel("Planner", "◫", plannerViewModel, Select));
        NavItems.Add(new NavItemViewModel("Insights", "◈", new InsightsPageViewModel(), Select));
        NavItems.Add(new NavItemViewModel("Settings", "◎", new SettingsPageViewModel(), Select));

        NavItems[0].IsSelected = true;
        CurrentPage = NavItems[0].PageViewModel;

        _ = InitializePendingOverlaysAsync();
    }

    // Startup catch-up, run once. Two independent things can already be true by the
    // time this Shell finishes constructing, and both need to be queued in the right
    // order before anything is shown:
    //
    // 1. Any Completed-but-undismissed or Missed-and-unacknowledged post-session,
    //    fetched chronologically (by EndedAt) via GetUnacknowledgedPostSessionsQuery —
    //    these always go first, oldest first, so the user resolves what already
    //    happened before being dropped into what's happening now.
    // 2. A Focus Block's Ritual requirement. This is also a startup race:
    //    ScheduledEnforcementService's heartbeat starts (via AppHost.StartAsync() in
    //    Program.Main) before this constructor runs and subscribes to
    //    RitualRequirementChanged above. If a Focus Block is already active at launch,
    //    the very first heartbeat tick can call SetRitualRequired(true, ...) with no
    //    subscriber listening — and since that method only raises its event on a
    //    required-state transition (a later tick for the same still-pending block is a
    //    no-op), that notification is lost for good once the block's ID stops changing.
    //    Catching up here, enqueued behind any pending post-sessions above, covers both
    //    races with one mechanism instead of two.
    //
    // ActiveSession is checked too, defensively, even though Program.Main now awaits
    // ISessionStateService.InitializeAsync() before either the heartbeat or this
    // constructor can run — the Ritual must never appear over an active session, and
    // this costs nothing to assert here too.
    private async Task InitializePendingOverlaysAsync()
    {
        var unacknowledged = await _getUnacknowledgedPostSessions.HandleAsync(new GetUnacknowledgedPostSessionsQuery());
        foreach (var postSession in unacknowledged)
            _pendingOverlays.Enqueue(new PendingPostSession(postSession));

        if (_sessionState.IsRitualRequired
            && _sessionState.ActiveSession is null
            && _sessionState.PendingFocusBlockId.HasValue)
        {
            _pendingOverlays.Enqueue(new PendingRitual(_sessionState.PendingFocusBlockId.Value));
        }

        TryDrainQueue();
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
        bool isLateToBlock = false;
        TimeSpan lateBy = default;

        if (focusBlockId.HasValue)
        {
            var context = await _getFocusBlockRitualContext.HandleAsync(
                new GetFocusBlockRitualContextQuery { FocusBlockId = focusBlockId.Value });

            if (context is not null && context.AlreadyHasSession)
            {
                // A Session already exists for this block — its ritual requirement is
                // stale (e.g. the sweep already marked it Missed under a different "now"
                // than whatever triggered this call, see DESIGN_GAP_HISTORY.md Gap 21's
                // addenda). Opening the Ritual here would only guarantee a rejection at
                // Hatsu; decline silently and let whatever else is queued proceed instead.
                TryDrainQueue();
                return;
            }

            lockedPreset = context?.Preset;
            lockedSchedule = context?.Schedule;
            isLateToBlock = context?.IsLate ?? false;
            lateBy = context?.LateBy ?? default;
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
            onCancel: isPlanned ? () => { } : ExitRitual,
            isLateToBlock: isLateToBlock,
            lateBy: lateBy);
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
            _pendingOverlays.Enqueue(new PendingPostSession(postSession));

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
            _pendingOverlays.Enqueue(new PendingPostSession(postSession));

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

    // Fires when the enforcement heartbeat's periodic sweep catches a block whose window
    // closed with no ritual done — a case the live edge-triggered ritual-required tracking
    // never had a chance to observe (see ScheduledEnforcementService). Same queue, same
    // triage batching as every other post-session; the only difference from the live path
    // is where the PostSessionDto came from.
    private void OnPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions)
    {
        foreach (var postSession in postSessions)
            _pendingOverlays.Enqueue(new PendingPostSession(postSession));
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
            // Triage batching stays scoped to Missed sessions only — a Completed-but-
            // undismissed post-session can never exceed one at a time (the overlay itself
            // blocks starting a second session before the first is dismissed), so it has
            // no batching case to cover and must never be swept into Triage's "skip all".
            var missedCount = _pendingOverlays
                .OfType<PendingPostSession>()
                .Count(p => p.PostSession.Status == SessionStatus.Missed);
            if (missedCount > 3)
            {
                ShowTriage(missedCount);
                return;
            }
        }

        switch (_pendingOverlays.Dequeue())
        {
            case PendingPostSession postSession:
                ShowPostSession(postSession.PostSession);
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
                _pendingOverlays.Enqueue(new PendingPostSession(postSession));

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
        // Only drops pending Missed post-sessions — a pending Completed one (dismissed
        // via "Review each" or simply not yet reached) is untouched, since Triage never
        // counted it and skipping it would silently discard an unsaved reflection prompt.
        var kept = _pendingOverlays
            .Where(o => o is not PendingPostSession postSession || postSession.PostSession.Status != SessionStatus.Missed)
            .ToList();
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

        // The wizard is opened from the Planner page as a page-scoped action, not an
        // app-wide interrupt like Ritual/PostSession/Triage — so unlike those overlays,
        // closing it must not force navigation back to Main. Only drain the queue (and
        // therefore only navigate) when something actually queued up behind it, e.g. a
        // Ritual that became due while the wizard was open.
        if (_pendingOverlays.Count > 0)
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
        PostSessionViewModel = new PostSessionViewModel(data, _saveReflection, _acknowledgePostSession, ExitPostSession);
        IsPostSessionActive = true;
    }

    private void ExitPostSession()
    {
        IsPostSessionActive = false;
        PostSessionViewModel = null;
        _sessionState.NotifyPostSessionAcknowledged();
        TryDrainQueue();
    }
}