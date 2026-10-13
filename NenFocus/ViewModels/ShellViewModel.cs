using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.Queries.GetSessionSnapshotForBlock;
using NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;
using NenFocus.Application.Queries.GetUnacknowledgedPostContracts;
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
using NenFocus.Application.UseCases.Sessions.SaveReflection;
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
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IActiveSessionState _sessionState;
    private readonly IRitualPrompt _ritualPrompt;
    private readonly IRecapRelay _recaps;
    private readonly IActiveSessionMonitor _monitor;
    private readonly IPlannerClockService _plannerClock;
    private readonly ISystemClock _clock;

    private readonly Queue<PendingOverlay> _pendingOverlays = new();
    private bool _suppressTriage;

    // The block the open Ritual is for; null when none is open or it is a spontaneous one.
    private Guid? _ritualBlockId;

    private abstract record PendingOverlay;
    private sealed record PendingPostSession(PostSessionDto PostSession) : PendingOverlay;
    private sealed record PendingRitual(Guid FocusBlockId) : PendingOverlay;
    private sealed record PendingPostContract(NenContractRecapDto Recap) : PendingOverlay;

    [ObservableProperty] private ViewModelBase _currentPage = null!;
    [ObservableProperty] private bool _isRitualActive;
    [ObservableProperty] private RitualShellViewModel? _ritualViewModel;
    [ObservableProperty] private bool _isPostSessionActive;
    [ObservableProperty] private PostSessionViewModel? _postSessionViewModel;
    [ObservableProperty] private bool _isPostContractActive;
    [ObservableProperty] private PostContractViewModel? _postContractViewModel;
    [ObservableProperty] private bool _isTriageActive;
    [ObservableProperty] private MissedSessionsTriageViewModel? _triageViewModel;
    [ObservableProperty] private bool _isContractWizardActive;
    [ObservableProperty] private ContractWizardViewModel? _contractWizardViewModel;

    /// <summary>Drives the sidebar/page-content IsVisible bindings — hidden behind either
    /// full-width overlay, same as IsPostSessionActive alone used to gate before Post-Contract
    /// existed.</summary>
    public bool IsMainContentVisible => !IsPostSessionActive && !IsPostContractActive;

    partial void OnIsPostSessionActiveChanged(bool value) => OnPropertyChanged(nameof(IsMainContentVisible));
    partial void OnIsPostContractActiveChanged(bool value) => OnPropertyChanged(nameof(IsMainContentVisible));

    public DebugClockViewModel DebugClockViewModel { get; }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public ShellViewModel(
       IQueryDispatcher queryDispatcher,
       ICommandDispatcher commandDispatcher,
       IActiveSessionState sessionState,
       IRitualPrompt ritualPrompt,
       IRecapRelay recaps,
       IActiveSessionMonitor monitor,
       IPlannerClockService plannerClock,
       ISystemClock clock,
       IDebugClockController debugClockController)
    {
        _queryDispatcher = queryDispatcher;
        _commandDispatcher = commandDispatcher;
        _sessionState = sessionState;
        _ritualPrompt = ritualPrompt;
        _recaps = recaps;
        _monitor = monitor;
        _plannerClock = plannerClock;
        _clock = clock;
        DebugClockViewModel = new DebugClockViewModel(clock, debugClockController);

        _ritualPrompt.Changed += (s, awaitingBlockId) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnRitualPromptChanged(awaitingBlockId));
        _recaps.PostSessionsReady += (s, postSessions) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnPostSessionsReady(postSessions));
        _recaps.PostContractsReady += (s, recaps) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnPostContractsReady(recaps));

        void Select(NavItemViewModel selected)
        {
            foreach (var item in NavItems) item.IsSelected = false;
            selected.IsSelected = true;
            CurrentPage = selected.PageViewModel;
        }

        NavItems.Add(new NavItemViewModel("Main", "⊹", new MainPageViewModel(() => StartRitual(), ShowPostSession, _sessionState, _monitor, _commandDispatcher, _clock), Select));
        var presetsPageViewModel = new PresetsPageViewModel(
            new BlockPresetPageViewModel(_queryDispatcher, _commandDispatcher),
            new ContractPresetPageViewModel(_queryDispatcher, _commandDispatcher));
        NavItems.Add(new NavItemViewModel("Presets", "◻", presetsPageViewModel, Select));
        var plannerViewModel = new PlannerPageViewModel(_queryDispatcher, _commandDispatcher, _plannerClock, _sessionState, _recaps, _clock);
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
    //    Program.Main) before this constructor runs and subscribes to the Ritual
    //    prompt's Changed above. If a Focus Block is already active at launch, the very
    //    first heartbeat tick can name it with no subscriber listening — and since the
    //    prompt only raises Changed when its answer changes (a later tick for the same
    //    still-pending block is silent), that notification is lost for good.
    //    Catching up here, enqueued behind any pending post-sessions above, covers both
    //    races with one mechanism instead of two.
    //
    // ActiveSession is checked too, defensively, even though Program.Main now awaits
    // IActiveSessionState.RefreshAsync() before either the heartbeat or this
    // constructor can run — the Ritual must never appear over an active session, and
    // this costs nothing to assert here too.
    private async Task InitializePendingOverlaysAsync()
    {
        var unacknowledged = await _queryDispatcher.QueryAsync(new GetUnacknowledgedPostSessionsQuery());
        foreach (var postSession in unacknowledged)
            _pendingOverlays.Enqueue(new PendingPostSession(postSession));

        // Enqueued behind post-sessions above — same "resolve what already happened before
        // what's happening now" ordering, applied one level further (a contract's own recap
        // is even less time-sensitive than a session's).
        var unacknowledgedContracts = await _queryDispatcher.QueryAsync(new GetUnacknowledgedPostContractsQuery());
        foreach (var recap in unacknowledgedContracts)
            _pendingOverlays.Enqueue(new PendingPostContract(recap));

        if (_ritualPrompt.PendingFocusBlockId is { } awaitingBlockId && _sessionState.ActiveSession is null)
            _pendingOverlays.Enqueue(new PendingRitual(awaitingBlockId));

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
            var context = await _queryDispatcher.QueryAsync(
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
            queryDispatcher: _queryDispatcher,
            commandDispatcher: _commandDispatcher,
            clock: _clock,
            originatingBlockId: focusBlockId,
            lockedPreset: lockedPreset,
            lockedSchedule: lockedSchedule,
            onComplete: ExitRitualAndGoToMain,
            onCancel: isPlanned ? () => { } : ExitRitual,
            isLateToBlock: isLateToBlock,
            lateBy: lateBy);
        _ritualBlockId = focusBlockId;
        IsRitualActive = true;
    }

    // The Ritual follows the prompt (Gap 92, A3, D′): open one when the prompt names a block,
    // close it when the prompt stops naming that block, whatever the reason — its window
    // closed (the heartbeat's sweep has already queued the missed screen, earlier in the same
    // tick) or its session started. Back-to-back blocks keep their order: the previous
    // block's missed screen is already queued, so the next block's Ritual goes behind it.
    private void OnRitualPromptChanged(Guid? awaitingBlockId)
    {
        var closed = false;
        if (IsRitualActive && _ritualBlockId is { } openBlockId && openBlockId != awaitingBlockId)
        {
            CloseRitualOverlay();
            closed = true;
        }

        if (awaitingBlockId is { } blockId && _ritualBlockId != blockId)
            _pendingOverlays.Enqueue(new PendingRitual(blockId));
        else if (!closed)
            return;

        TryDrainQueue();
    }

    // Fires when the enforcement heartbeat's sweep catches a block whose window closed with
    // no ritual done — the only way a missed screen arrives while the app runs (Gap 92, A3,
    // D′). Same queue, same triage batching as every other post-session.
    private void OnPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions)
    {
        foreach (var postSession in postSessions)
            _pendingOverlays.Enqueue(new PendingPostSession(postSession));
        TryDrainQueue();
    }

    private void TryDrainQueue()
    {
        if (IsRitualActive || IsPostSessionActive || IsPostContractActive || IsTriageActive || IsContractWizardActive) return;

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
                HandlePendingRitual(ritual.FocusBlockId);
                break;
            case PendingPostContract postContract:
                ShowPostContract(postContract.Recap);
                break;
        }
    }

    private void HandlePendingRitual(Guid focusBlockId)
    {
        // Time may have passed while this sat behind another overlay. If the prompt no longer
        // names the block, it no longer awaits its Ritual — its window closed (the sweep has
        // queued the missed screen) or its session started — so there is nothing to open.
        if (_ritualPrompt.PendingFocusBlockId != focusBlockId)
        {
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
        _ritualBlockId = null;
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
        PostSessionViewModel = new PostSessionViewModel(data, _commandDispatcher, ExitPostSession, _clock.TimeZone);
        IsPostSessionActive = true;
    }

    private void ExitPostSession()
    {
        IsPostSessionActive = false;
        PostSessionViewModel = null;
        _recaps.NotifyPostSessionAcknowledged();
        TryDrainQueue();
    }

    // A contract ended — completed by the sweep or the user, or broken — and its recap is owed
    // (NenContractEndedEventHandler, Gap 110). Queued like any overlay: shown now if nothing
    // covers the screen, else after what does.
    private void OnPostContractsReady(IReadOnlyList<NenContractRecapDto> recaps)
    {
        foreach (var recap in recaps)
            _pendingOverlays.Enqueue(new PendingPostContract(recap));
        TryDrainQueue();
    }

    private void ShowPostContract(NenContractRecapDto recap)
    {
        PostContractViewModel = new PostContractViewModel(recap, _commandDispatcher, ExitPostContract);
        IsPostContractActive = true;
    }

    private void ExitPostContract()
    {
        IsPostContractActive = false;
        PostContractViewModel = null;
        TryDrainQueue();
    }
}