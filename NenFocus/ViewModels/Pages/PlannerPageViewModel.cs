using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.Queries.GetActiveNenContract;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.Queries.GetScheduledNenContracts;
using NenFocus.Application.Queries.GetSessionSnapshotForBlock;
using NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Components;
using NenFocus.ViewModels.Contracts;

namespace NenFocus.ViewModels.Pages;

public partial class PlannerPageViewModel : ViewModelBase, IDisposable
{
    public const double HourRowHeight = 56.0;
    public const int FirstHour = 0;
    public const int LastHour = 24;
    public const int TotalHours = LastHour - FirstHour;
    public double GridContentHeight => HourRowHeight * TotalHours;

    private readonly IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> _getPresets;
    private readonly IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> _getBlocksForDay;
    private readonly ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> _createBlock;
    private readonly ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> _updateBlock;
    private readonly ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> _deleteBlock;
    private readonly ICommandHandler<SplitPlannedBlockCommand, SplitPlannedBlockResult> _splitBlock;
    private readonly ICommandHandler<SealNenContractCommand, SealNenContractResult> _sealContract;
    private readonly ICommandHandler<BreakNenContractCommand, BreakNenContractResult> _breakContract;
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _computePlannedFocusHours;
    private readonly IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> _getContractPresets;
    private readonly IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?> _getActiveContract;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;
    private readonly IQueryHandler<GetSuggestedRedemptionPlacementsQuery, IReadOnlyList<SuggestedRedemptionPlacementDto>> _getSuggestedRedemptionPlacements;
    private readonly IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> _checkOverlap;
    private readonly IQueryHandler<GetScheduledNenContractsQuery, IReadOnlyList<ScheduledNenContractDto>> _getScheduledContracts;
    private readonly IQueryHandler<GetSessionSnapshotForBlockQuery, SessionSnapshotDto?> _getSessionSnapshot;
    private readonly IQueryHandler<GetNenContractForEditQuery, NenContractEditDto?> _getContractForEdit;
    private readonly ICommandHandler<EditNenContractCommand, EditNenContractResult> _editContract;
    private readonly ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult> _deleteContract;
    private readonly IPlannerClockService _clockService;
    private readonly ISessionStateService _sessionState;

    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();
    private readonly List<BlockPresetDto> _allPresets = new();
    private readonly List<ContractDraftBlock> _draftBlocks = new();
    private Guid? _editingBlockId;
    private bool _editingIsDraft;
    private NenContractDraftRules? _draftRules;

    // Set only while drafting is seeded from an existing Scheduled contract (Edit flow).
    // Carries the original blocks (converted into draft tiles by BeginDrafting) so nothing
    // needs to be re-fetched at Seal time; cleared by ExitDraftingMode.
    private NenContractEditDto? _editingContractPayload;

    [ObservableProperty] private DateTime _weekStart;

    /// <summary>
    /// Returns the Grid.Column index for the red "Current Time" line.
    /// Column 0 is hour labels, so Column 1 is Monday, Column 7 is Sunday.
    /// </summary>
    public int TodayColumnIndex
    {
        get
        {
            int day = (int)_clockService.LocalNow.DayOfWeek;
            int mondayBasedIndex = (day == 0) ? 6 : day - 1;
            return mondayBasedIndex + 1;
        }
    }

    public ObservableCollection<WeekDayHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<HourLabelViewModel> HourLabels { get; } = new();
    public ObservableCollection<PlannedBlockTileViewModel> Tiles { get; } = new();
    public ObservableCollection<BlockPresetDto> AvailablePresets { get; } = new();
    public ObservableCollection<string> TimeOptions { get; } = new();
    // Regenerated from the current Start value (and Focus/Background type, for the 12h
    // cap) whenever either changes — see RegenerateEndTimeOptions.
    public ObservableCollection<PlannerEndTimeOptionDto> EndTimeOptions { get; } = new();

    // What the Start/End AutoCompleteBox dropdowns actually display — recomputed on every
    // keystroke. A complete, valid time-of-day shows the FULL underlying list (the View's
    // code-behind scrolls it to a ~5-item window around the current value; see
    // ScrollDropdownToWindow in PlannerPageView.axaml.cs); a partial/invalid typed value
    // instead shows every entry starting with it, unbounded. See
    // UpdateVisibleStartTimeOptions/UpdateVisibleEndTimeOptions.
    //
    // [ObservableProperty] (whole-collection reassignment) rather than a fixed instance
    // mutated via Clear()+Add() — AutoCompleteBox keeps its own internal selection state
    // synced to its ItemsSource, and clearing a live-bound collection out from under an
    // active selection crashes deep in Avalonia's SelectingItemsControl (confirmed via a
    // real repro: placing an End time that rolls past midnight, where the newly-generated
    // list is drastically shorter than the previous one, threw
    // ArgumentOutOfRangeException inside AutoCompleteBox.ClearView()). Reassigning the
    // property lets Avalonia's ordinary ItemsSource-changed path tear down and rebuild
    // cleanly instead of mutating a collection an active selection still points into.
    [ObservableProperty] private ObservableCollection<string> _visibleStartTimeOptions = new();
    [ObservableProperty] private ObservableCollection<PlannerEndTimeOptionDto> _visibleEndTimeOptions = new();

    // The index within VisibleStartTimeOptions/VisibleEndTimeOptions the dropdown should be
    // scrolled to on open — the parsed current value's position when the full list is
    // showing, null when a partial/invalid filter is showing instead (nothing to center on).
    // Read by the View's code-behind; not itself bound to anything in XAML.
    public int? StartTimeScrollIndex { get; private set; }
    public int? EndTimeScrollIndex { get; private set; }

    // Split dialog's four time fields get the identical treatment, mirroring Start's own
    // plain flat-list pattern (no duration labels — these are ordinary times, not offsets).
    [ObservableProperty] private ObservableCollection<string> _visibleSplitFirstStartTimeOptions = new();
    [ObservableProperty] private ObservableCollection<string> _visibleSplitFirstEndTimeOptions = new();
    [ObservableProperty] private ObservableCollection<string> _visibleSplitSecondStartTimeOptions = new();
    [ObservableProperty] private ObservableCollection<string> _visibleSplitSecondEndTimeOptions = new();
    public int? SplitFirstStartTimeScrollIndex { get; private set; }
    public int? SplitFirstEndTimeScrollIndex { get; private set; }
    public int? SplitSecondStartTimeScrollIndex { get; private set; }
    public int? SplitSecondEndTimeScrollIndex { get; private set; }
    // Per-day floor badge lookup, keyed by date — feeds the day-header badges. Populated
    // either by RefreshDayFloorPreviewsAsync (drafting) or RefreshActiveContractAsync
    // (sealed/active contract); the two are mutually exclusive for any viewed week, since
    // an in-effect contract can never overlap the date range being drafted. Not an
    // ObservableCollection — the whole dictionary is replaced on refresh so day-header
    // bindings (which look up by key) see one atomic update instead of N incremental ones.
    [ObservableProperty] private Dictionary<DateOnly, DayFloorPreviewViewModel> _dayFloorByDate = new();

    // "X of Y met" / "All daily floors met", excluding rest days — shown in the drafting
    // action bar. Purely a rephrasing of DayFloorByDate's own IsMet/IsRestDay flags.
    public string DraftFloorSummaryText
    {
        get
        {
            var relevant = DayFloorByDate.Values.Where(d => !d.IsRestDay).ToList();
            if (relevant.Count == 0) return string.Empty;
            var met = relevant.Count(d => d.IsMet);
            return met == relevant.Count ? "All daily floors met" : $"{met} of {relevant.Count} met";
        }
    }

    partial void OnDayFloorByDateChanged(Dictionary<DateOnly, DayFloorPreviewViewModel> value)
    {
        OnPropertyChanged(nameof(DraftFloorSummaryText));
        foreach (var header in DayHeaders)
            header.DayFloorStatus = value.GetValueOrDefault(DateOnly.FromDateTime(header.Date));
    }

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "New Block";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private DateTimeOffset _formDate;

    [ObservableProperty] private double _currentTimeY;
    [ObservableProperty] private bool _isTodayInView;

    [ObservableProperty] private string _formStartTime = "09:00";
    [ObservableProperty] private string _formEndTime = "10:00";

    // Google-Calendar-style: a typed/picked End at or before Start resolves to the next
    // day rather than being rejected outright. Non-draft form only — drafting a contract
    // keeps today's same-day-only behavior unchanged (see ValidateDraftPlacement's own
    // non-day-segmented Work Hours check, deliberately not extended to cross-midnight
    // either, per Gap K Part 1's own scope note).
    [ObservableProperty] private bool _formEndRollsToNextDay;
    [ObservableProperty] private string _formEndDateText = string.Empty;

    // Duration preserved across a Start edit (End shifts to keep it); re-derived whenever
    // End itself is edited. Set alongside FormStartTime/FormEndTime by SetFormTimes.
    private TimeSpan _preservedFormDuration;

    // Suppresses the shift-math reaction in OnFormStartTimeChanged/OnFormEndTimeChanged
    // for a programmatic assignment — either populating the form for an existing
    // block/draft, or the shift handler itself updating the other field — neither
    // represents a live user edit.
    private bool _isPopulatingFormTimes;

    // Live syntax-only validation (can this text parse as a time-of-day at all) — turns
    // the field red; on LostFocus (wired from the View's code-behind) an invalid field
    // snaps back to the last value that successfully parsed. Independent of Save-time
    // business-rule validation (12h cap, Work Hours, floor, overlap), which is unchanged.
    [ObservableProperty] private bool _isFormStartTimeInvalid;
    [ObservableProperty] private bool _isFormEndTimeInvalid;
    private string _lastValidFormStartTime = "09:00";
    private string _lastValidFormEndTime = "10:00";

    [ObservableProperty] private bool _isFocusBlock = true;
    [ObservableProperty] private BlockPresetDto? _selectedPreset;
    [ObservableProperty] private bool _isStartLocked;
    [ObservableProperty] private bool _isEditingCompleted;
    [ObservableProperty] private bool _isEditingBlockEnded;
    [ObservableProperty] private bool _isEditingBlockContractLocked;
    [ObservableProperty] private bool _isEditingBlockDailyFlexibility;
    [ObservableProperty] private string? _dragError;

    [ObservableProperty] private bool _isSnapshotVisible;
    [ObservableProperty] private PlannedBlockDto? _snapshotBlock;
    [ObservableProperty] private SessionSnapshotDto? _snapshotSession;
    [ObservableProperty] private bool _isConfirmDeleteSnapshotVisible;
    [ObservableProperty] private string? _deleteSnapshotErrorMessage;

    // Passive preview only, refreshed whenever preset/date/time change — no user action
    // needed and nothing sent on Save. CreatePlannedBlockCommandHandler runs the exact same
    // ComputeRedemptionAttributionQuery itself when the block is actually created, so this
    // can never drift from what Save will really do.
    [ObservableProperty] private string? _redemptionSuggestionNotice;

    // ── Split Block ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSplitDialogVisible;
    [ObservableProperty] private string _splitFirstStartTime = "09:00";
    [ObservableProperty] private string _splitFirstEndTime = "10:00";
    [ObservableProperty] private string _splitSecondStartTime = "10:00";
    [ObservableProperty] private string _splitSecondEndTime = "11:00";
    [ObservableProperty] private string? _splitError;

    // Duration-preserving shift, mirroring the main form's own PlannerTimeShiftMath
    // wiring — independently per piece, since First and Second never affect each other.
    private TimeSpan _preservedSplitFirstDuration;
    private TimeSpan _preservedSplitSecondDuration;
    // Covers both OpenSplitDialog's initial population and each shift handler's own
    // programmatic update of the other field in its pair — neither is a live user edit.
    private bool _isPopulatingSplitTimes;

    // ── Nen Contract wizard + drafting mode ───────────────────────────────────
    [ObservableProperty] private bool _isWizardVisible;
    [ObservableProperty] private ContractWizardViewModel? _wizardViewModel;
    [ObservableProperty] private bool _isDraftingContract;
    [ObservableProperty] private string? _draftContractName;
    [ObservableProperty] private bool _isSealing;
    [ObservableProperty] private string? _sealErrorMessage;
    [ObservableProperty] private bool _isConfirmDiscardDraftVisible;
    [ObservableProperty] private string _confirmDiscardDraftMessage = string.Empty;

    // ── Active-contract banner (persists for the contract's full duration, independent
    // of drafting mode) ─────────────────────────────────────────────────────────────
    [ObservableProperty] private ActiveNenContractDto? _activeContract;
    [ObservableProperty] private bool _isConfirmCancelContractVisible;
    [ObservableProperty] private string _confirmCancelContractMessage = string.Empty;
    [ObservableProperty] private string? _cancelContractErrorMessage;

    // ── Nen Contract popover (replaces the old top-right "Make Nen Contract" button) ──
    [ObservableProperty] private bool _isNenContractPopoverVisible;
    public ObservableCollection<ScheduledNenContractDto> ScheduledContracts { get; } = new();
    public bool HasNoContracts => ActiveContract is null && ScheduledContracts.Count == 0;

    [ObservableProperty] private bool _isConfirmDeleteContractVisible;
    [ObservableProperty] private string _confirmDeleteContractMessage = string.Empty;
    [ObservableProperty] private string? _deleteContractErrorMessage;

    private Guid? _contractIdPendingDelete;

    // ── Cover Deficit popover (mirrors the Nen Contract popover's button+Popup shape) ──
    [ObservableProperty] private bool _isCoverDeficitPopoverVisible;
    [ObservableProperty] private string? _coverDeficitErrorMessage;
    public ObservableCollection<SuggestedRedemptionPlacementDto> CoverDeficitOptions { get; } = new();
    public bool HasCoverableDeficit => CoverDeficitOptions.Count > 0;

    [ObservableProperty] private bool _isConfirmPartialRedemptionVisible;
    [ObservableProperty] private string _confirmPartialRedemptionMessage = string.Empty;
    private SuggestedRedemptionPlacementDto? _pendingPartialRedemption;

    public bool HasActiveContract => ActiveContract is not null;

    // AllowCancellation is deliberately not enforced here (interim, 2026-08-18) — mirrors
    // NenContract.Break's own comment; every active contract can be cancelled until Strict
    // Mode exists as the real gate. Only presence of an active contract matters now.
    public bool CanCancelActiveContract => HasActiveContract;

    // One format for every strictness rule — total completed vs. total committed hours,
    // plus outstanding deficit. Uniform across Locked/DailyFlexibility/PeriodTimeBudget
    // (with or without ContractFloor) since TotalFloorHours/TotalCompletedHours are
    // already computed the same way for all of them.
    public string ActiveContractBannerText => ActiveContract is not { } c
        ? string.Empty
        : $"Contract: {c.Name} — Day {c.DayNumber}/{c.TotalDays} — " +
          $"{c.TotalCompletedHours:0.#}h / {c.TotalFloorHours:0.#}h completed — {c.DeficitHours:0.#}h deficit";

    // 3-second live poll for the day-header floor badges while an active contract is in
    // view — so today's badge reflects an in-progress session's elapsed hours without
    // waiting for the next week-nav/seal/session-end refresh. No page-visibility gating:
    // this ViewModel already lives for the app's whole lifetime (not recreated on
    // navigation, and there's no such hook anywhere in this codebase), and the cost is
    // comparable to ScheduledEnforcementService's own always-on 5s heartbeat. Started/
    // stopped by OnActiveContractChanged, not by a page-visible/hidden signal.
    private readonly System.Timers.Timer _floorStatusPollTimer = new(3000) { AutoReset = true };

    partial void OnActiveContractChanged(ActiveNenContractDto? value)
    {
        OnPropertyChanged(nameof(HasActiveContract));
        OnPropertyChanged(nameof(ActiveContractBannerText));
        OnPropertyChanged(nameof(CanCancelActiveContract));
        OnPropertyChanged(nameof(HasNoContracts));

        if (value is not null) _floorStatusPollTimer.Start();
        else _floorStatusPollTimer.Stop();
    }

    // Scoped to the currently viewed week — the banner only shows when a contract's date
    // range overlaps the week actually on screen, not just "some contract is in effect
    // somewhere." Must be re-run on every week navigation, not only on init/seal/session-end.
    private async Task RefreshActiveContractAsync()
    {
        var weekStartDate = DateOnly.FromDateTime(WeekStart);
        var weekEndDate = weekStartDate.AddDays(6);
        ActiveContract = await _getActiveContract.HandleAsync(new GetActiveNenContractQuery(weekStartDate, weekEndDate));

        // Drafting owns DayFloorByDate while it's active (RefreshDayFloorPreviewsAsync) —
        // an in-effect contract can never overlap the date range being drafted, so this
        // never runs concurrently with a real draft, but skip anyway rather than relying
        // on that invariant holding forever.
        if (IsDraftingContract) return;

        DayFloorByDate = ActiveContract?.DayStatuses.ToDictionary(
            d => d.Date,
            d => new DayFloorPreviewViewModel(
                d.Date, d.CurrentHours, d.IsRestDay ? null : d.FloorHours, d.IsRestDay, d.Timing,
                ActiveContract.StrictnessRule, ActiveContract.ContractFloorHours.HasValue))
            ?? new Dictionary<DateOnly, DayFloorPreviewViewModel>();

        await RefreshCoverDeficitOptionsAsync();
    }

    private async Task RefreshCoverDeficitOptionsAsync()
    {
        CoverDeficitOptions.Clear();
        if (ActiveContract is { } contract)
        {
            var suggestions = await _getSuggestedRedemptionPlacements.HandleAsync(
                new GetSuggestedRedemptionPlacementsQuery(contract.Id, _clockService.LocalNow.ToUniversalTime()));
            foreach (var suggestion in suggestions)
                CoverDeficitOptions.Add(suggestion);
        }
        OnPropertyChanged(nameof(HasCoverableDeficit));
    }

    [RelayCommand]
    private void ToggleCoverDeficitPopover()
    {
        if (CoverDeficitOptions.Count == 0) return;
        if (CoverDeficitOptions.Count == 1)
        {
            _ = CoverDeficitAsync(CoverDeficitOptions[0]);
            return;
        }
        IsCoverDeficitPopoverVisible = !IsCoverDeficitPopoverVisible;
    }

    [RelayCommand]
    private async Task CoverDeficitAsync(SuggestedRedemptionPlacementDto option)
    {
        IsCoverDeficitPopoverVisible = false;
        CoverDeficitErrorMessage = null;

        if (!option.CanFullyCover)
        {
            _pendingPartialRedemption = option;
            ConfirmPartialRedemptionMessage =
                $"This starts now and won't fully cover the {option.TotalRemainingDeficitMinutes:0.#} min owed for " +
                $"{option.PresetName} — the rest will still show as an outstanding deficit. Create it anyway?";
            IsConfirmPartialRedemptionVisible = true;
            return;
        }

        await CreateRedemptionTileAsync(option);
    }

    [RelayCommand]
    private async Task ConfirmPartialRedemptionAsync()
    {
        IsConfirmPartialRedemptionVisible = false;
        if (_pendingPartialRedemption is { } option)
            await CreateRedemptionTileAsync(option);
        _pendingPartialRedemption = null;
    }

    [RelayCommand]
    private void CancelPartialRedemption()
    {
        IsConfirmPartialRedemptionVisible = false;
        _pendingPartialRedemption = null;
    }

    private async Task CreateRedemptionTileAsync(SuggestedRedemptionPlacementDto option)
    {
        var result = await _createBlock.HandleAsync(new CreatePlannedBlockCommand
        {
            PresetId = option.PresetId,
            BlockPresetType = BlockPresetType.Focus,
            Start = option.SuggestedStart,
            End = option.SuggestedEnd
        });

        if (!result.Success)
        {
            CoverDeficitErrorMessage = result.ErrorMessage;
            return;
        }

        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    [RelayCommand]
    private void RequestCancelContract()
    {
        if (ActiveContract is null) return;
        CancelContractErrorMessage = null;
        ConfirmCancelContractMessage = "Are you sure you want to cancel this contract? This cannot be undone.";
        IsConfirmCancelContractVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCancelContractAsync()
    {
        if (ActiveContract is null) { IsConfirmCancelContractVisible = false; return; }

        var result = await _breakContract.HandleAsync(new BreakNenContractCommand { ContractId = ActiveContract.Id });
        IsConfirmCancelContractVisible = false;

        if (!result.Success)
        {
            CancelContractErrorMessage = result.ErrorMessage;
            return;
        }

        IsNenContractPopoverVisible = false;
        await RefreshActiveContractAsync();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private void CancelCancelContract() => IsConfirmCancelContractVisible = false;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Block";

    // Mirrors DeletePlannedBlockValidator's rejection conditions exactly — sourced from
    // dto.HasEnded / dto.IsUnderInEffectContract (Application-computed, same predicates the
    // validator calls) and the live active-session noticeboard, not re-derived here.
    public bool CanDeleteFromForm =>
        IsEditing && !IsEditingBlockEnded && !IsEditingBlockContractLocked
        && _sessionState.ActiveSession?.OriginatingBlockId != _editingBlockId;

    // True when the editing block is either currently running (active session) or already completed.
    // Controls visibility of the Save button and disables form inputs.
    public bool IsBlockLocked =>
        IsEditing && (IsEditingCompleted || _sessionState.ActiveSession?.OriginatingBlockId == _editingBlockId);

    public bool CanSaveBlock => !IsBlockLocked;

    // Mirrors DeletePlannedBlockValidator's sole remaining rejection condition for a done
    // block: any Nen Contract linkage, permanent, regardless of the contract's own status.
    public bool CanDeleteSnapshotBlock => SnapshotBlock is not null && SnapshotBlock.ContractId is null;

    public bool HasSnapshotSession => SnapshotSession is not null;

    // A Background block never has a Session (no Ritual) — the "no session was started"
    // message only makes sense for a Focus block that was Missed (or Cancelled, though
    // Cancelled blocks never reach the Planner tile list at all).
    public bool ShowSnapshotNoSessionMessage =>
        SnapshotBlock is not null && SnapshotBlock.BlockPresetType == BlockPresetType.Focus && SnapshotSession is null;

    // Mirrors SplitPlannedBlockValidator's gating conditions exactly — sourced from
    // dto.IsUnderInEffectContract + dto.ContractStrictnessRule (Application-computed),
    // IsStartLocked (the future-only guard — a block that hasn't started yet always has
    // Start > now), IsEditingCompleted (a completed ritual means a session ran), IsFocusBlock
    // (Split is Focus-only — a Background block has no session and no Daily Floor
    // relevance), and the live active-session noticeboard. The validator remains the sole
    // authority; this only gates button visibility, same relationship CanDeleteFromForm has
    // to its validator.
    public bool CanSplitBlock =>
        IsEditing && !_editingIsDraft && IsFocusBlock
        && IsEditingBlockContractLocked && IsEditingBlockDailyFlexibility
        && !IsStartLocked && !IsEditingCompleted
        && _sessionState.ActiveSession?.OriginatingBlockId != _editingBlockId;

    // Deliberately NOT gated on session-active — confirmed with the architect: the only
    // reason to disable Make/Edit is a genuine overlay collision (a form or the wizard
    // already open), not merely a session running. This differs from the old top-right
    // button's original guard, which this popover fully replaces.
    public bool CanOpenContractWizard =>
        !IsFormVisible && !IsWizardVisible && !IsDraftingContract;

    // Shell renders the wizard as a full-screen overlay (mirrors the Ritual overlay), so it
    // needs to know when one opens/closes even though this ViewModel still owns construction
    // and the drafting handoff — same event-bridge shape as ISessionStateService's
    // RitualRequirementChanged, which ShellViewModel already consumes.
    public event Action<ContractWizardViewModel>? ContractWizardOpened;
    public event Action? ContractWizardClosed;

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(CanDeleteFromForm));
        OnPropertyChanged(nameof(IsBlockLocked));
        OnPropertyChanged(nameof(CanSaveBlock));
        OnPropertyChanged(nameof(CanSplitBlock));
    }

    partial void OnIsEditingCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBlockLocked));
        OnPropertyChanged(nameof(CanSaveBlock));
        OnPropertyChanged(nameof(CanSplitBlock));
    }

    partial void OnIsEditingBlockEndedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeleteFromForm));
    }

    partial void OnIsEditingBlockContractLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeleteFromForm));
        OnPropertyChanged(nameof(CanSplitBlock));
    }

    partial void OnIsEditingBlockDailyFlexibilityChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSplitBlock));
    }

    partial void OnIsStartLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSplitBlock));
    }

    partial void OnIsFormVisibleChanged(bool value) => OnPropertyChanged(nameof(CanOpenContractWizard));
    partial void OnIsWizardVisibleChanged(bool value) => OnPropertyChanged(nameof(CanOpenContractWizard));
    partial void OnIsDraftingContractChanged(bool value) => OnPropertyChanged(nameof(CanOpenContractWizard));

    public PlannerPageViewModel(
        IQueryHandler<GetAllBlockPresetsQuery, IReadOnlyList<BlockPresetDto>> getPresets,
        IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>> getBlocksForDay,
        ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult> createBlock,
        ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult> updateBlock,
        ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> deleteBlock,
        ICommandHandler<SplitPlannedBlockCommand, SplitPlannedBlockResult> splitBlock,
        ICommandHandler<SealNenContractCommand, SealNenContractResult> sealContract,
        ICommandHandler<BreakNenContractCommand, BreakNenContractResult> breakContract,
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours,
        IQueryHandler<GetAllContractPresetsQuery, IReadOnlyList<ContractPresetDto>> getContractPresets,
        IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?> getActiveContract,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution,
        IQueryHandler<GetSuggestedRedemptionPlacementsQuery, IReadOnlyList<SuggestedRedemptionPlacementDto>> getSuggestedRedemptionPlacements,
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap,
        IQueryHandler<GetScheduledNenContractsQuery, IReadOnlyList<ScheduledNenContractDto>> getScheduledContracts,
        IQueryHandler<GetSessionSnapshotForBlockQuery, SessionSnapshotDto?> getSessionSnapshot,
        IQueryHandler<GetNenContractForEditQuery, NenContractEditDto?> getContractForEdit,
        ICommandHandler<EditNenContractCommand, EditNenContractResult> editContract,
        ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult> deleteContract,
        IPlannerClockService clockService,
        ISessionStateService sessionState)
    {
        _getPresets = getPresets;
        _getBlocksForDay = getBlocksForDay;
        _createBlock = createBlock;
        _updateBlock = updateBlock;
        _deleteBlock = deleteBlock;
        _splitBlock = splitBlock;
        _sealContract = sealContract;
        _getScheduledContracts = getScheduledContracts;
        _getSessionSnapshot = getSessionSnapshot;
        _getContractForEdit = getContractForEdit;
        _editContract = editContract;
        _deleteContract = deleteContract;
        _breakContract = breakContract;
        _computePlannedFocusHours = computePlannedFocusHours;
        _getContractPresets = getContractPresets;
        _getActiveContract = getActiveContract;
        _computeRedemptionAttribution = computeRedemptionAttribution;
        _getSuggestedRedemptionPlacements = getSuggestedRedemptionPlacements;
        _checkOverlap = checkOverlap;
        _clockService = clockService;
        _sessionState = sessionState;
        _sessionState.SessionChanged += OnSessionChanged;
        _sessionState.PostSessionAcknowledged += OnPostSessionAcknowledged;

        for (int h = FirstHour; h < LastHour; h++)
            HourLabels.Add(new HourLabelViewModel(h));

        GenerateTimeOptions();

        WeekStart = StartOfWeek(_clockService.LocalNow.Date);
        BuildDayHeaders();

        _clockService.Tick += OnClockTick;
        UpdateCurrentTimePosition();

        _floorStatusPollTimer.Elapsed += OnFloorStatusPollTick;

        _ = InitAsync();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateCurrentTimePosition);
    }

    private void OnFloorStatusPollTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => _ = RefreshActiveContractAsync());
    }

    private void OnSessionChanged(object? sender, SessionDto? session)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsBlockLocked));
            OnPropertyChanged(nameof(CanSaveBlock));
            OnPropertyChanged(nameof(CanDeleteFromForm));
            OnPropertyChanged(nameof(CanOpenContractWizard));
            // Reload on both transitions: a session starting stamps the originating block's
            // RitualCompletedAt (its Outcome/tile display changes even though the block
            // hasn't ended yet), a session ending may resolve EndedEarly. Only a finished
            // session can move Day Met/deficit, so that refresh stays end-only.
            _ = LoadWeekAsync();
            if (session is null)
                _ = RefreshActiveContractAsync();
        });
    }

    // The heartbeat's background sweep can mark a block Missed with nobody watching live —
    // ShellViewModel surfaces it as a Post-Session popup (see ISessionStateService.
    // PostSessionsReady); this is the signal that its dismissal ("Done") is the right moment
    // to reload, since that's when the underlying data is actually known to have changed.
    private void OnPostSessionAcknowledged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => _ = LoadWeekAsync());
    }

    private void UpdateCurrentTimePosition()
    {
        var now = _clockService.LocalNow;
        var weekEnd = WeekStart.AddDays(7);
        IsTodayInView = now.Date >= WeekStart.Date && now.Date < weekEnd.Date;

        if (IsTodayInView)
            CurrentTimeY = PlannerLayout.GetCurrentTimeY(now, FirstHour, HourRowHeight);

        OnPropertyChanged(nameof(TodayColumnIndex));
    }

    private void GenerateTimeOptions()
    {
        TimeOptions.Clear();
        for (int h = 0; h < 24; h++)
        {
            for (int m = 0; m < 60; m += 15)
            {
                TimeOptions.Add($"{h:D2}:{m:D2}");
            }
        }
        TimeOptions.Add("24:00");
    }

    private async Task InitAsync()
    {
        await LoadPresetsAsync();
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    [RelayCommand]
    private async Task PreviousWeekAsync() => await NavigateWeekAsync(WeekStart.AddDays(-7));

    [RelayCommand]
    private async Task NextWeekAsync() => await NavigateWeekAsync(WeekStart.AddDays(7));

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        var today = StartOfWeek(_clockService.LocalNow.Date);
        if (today == WeekStart) return;
        await NavigateWeekAsync(today);
    }

    // While drafting a contract, week navigation is clamped to weeks that overlap the
    // contract's date range — dragging blocks outside the range makes no sense mid-draft.
    private async Task NavigateWeekAsync(DateTime candidateWeekStart)
    {
        if (IsDraftingContract && !WeekOverlapsDraftRange(candidateWeekStart)) return;

        WeekStart = candidateWeekStart;
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    private bool WeekOverlapsDraftRange(DateTime candidateWeekStart)
    {
        if (_draftRules is null) return true;
        var weekEnd = candidateWeekStart.AddDays(6);
        var rangeStart = _draftRules.StartDate.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = _draftRules.EndDate.ToDateTime(TimeOnly.MinValue);
        return weekEnd.Date >= rangeStart.Date && candidateWeekStart.Date <= rangeEnd.Date;
    }

    private void BuildDayHeaders()
    {
        var today = _clockService.LocalNow.Date;
        DayHeaders.Clear();
        for (int i = 0; i < 7; i++)
            DayHeaders.Add(new WeekDayHeaderViewModel(WeekStart.AddDays(i), today));
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }

    private async Task LoadPresetsAsync()
    {
        var presets = await _getPresets.HandleAsync(new GetAllBlockPresetsQuery());
        _allPresets.Clear();
        _allPresets.AddRange(presets);
        RefreshAvailablePresets();
    }

    private void RefreshAvailablePresets()
    {
        var requiredType = IsFocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;
        AvailablePresets.Clear();
        foreach (var p in _allPresets.Where(p => p.PresetType == requiredType))
            AvailablePresets.Add(p);
    }

    partial void OnIsFocusBlockChanged(bool value)
    {
        RefreshAvailablePresets();
        SelectedPreset = AvailablePresets.FirstOrDefault();
        OnPropertyChanged(nameof(CanSplitBlock));
        RegenerateEndTimeOptions();
        UpdateVisibleEndTimeOptions(FormEndTime);
    }

    partial void OnSelectedPresetChanged(BlockPresetDto? value) => _ = RefreshRedemptionPreviewAsync();
    partial void OnFormDateChanged(DateTimeOffset value) => _ = RefreshRedemptionPreviewAsync();
    partial void OnFormStartTimeChanged(string value)
    {
        _ = RefreshRedemptionPreviewAsync();

        IsFormStartTimeInvalid = !TryParseFormTime(value, out var newStart);
        if (!IsFormStartTimeInvalid) _lastValidFormStartTime = value;
        if (!IsFormStartTimeInvalid) RegenerateEndTimeOptions();
        UpdateVisibleStartTimeOptions(value);
        UpdateVisibleEndTimeOptions(FormEndTime); // End's own pool just regenerated (or didn't) — either way, re-window it

        if (_isPopulatingFormTimes || IsDraftingContract || IsFormStartTimeInvalid) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedFormDuration);
        _isPopulatingFormTimes = true;
        FormEndTime = $"{(int)newEnd.TotalHours:D2}:{newEnd.Minutes:D2}";
        _isPopulatingFormTimes = false;

        UpdateEndRolloverDisplay(newStart, newEnd);
    }

    partial void OnFormEndTimeChanged(string value)
    {
        _ = RefreshRedemptionPreviewAsync();

        IsFormEndTimeInvalid = !TryParseFormTime(value, out var newEnd);
        if (!IsFormEndTimeInvalid) _lastValidFormEndTime = value;
        UpdateVisibleEndTimeOptions(value);

        if (_isPopulatingFormTimes || IsDraftingContract || IsFormEndTimeInvalid) return;
        if (!TryParseFormTime(FormStartTime, out var start)) return;

        _preservedFormDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
        UpdateEndRolloverDisplay(start, newEnd);
    }

    /// <summary>
    /// Google-Calendar-style dropdown content: a complete, valid time-of-day shows the full
    /// underlying list — the View's code-behind scrolls the popup to a ~5-item window
    /// around it, rather than the ItemsSource itself being sliced down, so the user can
    /// still scroll to any other entry. A partial/invalid typed value instead shows every
    /// entry starting with it, unbounded. AutoCompleteBox's own built-in filtering is
    /// disabled (its TextFilter is wired to always match in the View's code-behind)
    /// specifically so it can't re-narrow this list back down to just the exact text typed —
    /// the same re-narrowing that made the dropdown show only the current value before this
    /// existed. Returns the index to scroll to (null when the partial/invalid branch is
    /// showing instead — nothing to center on).
    /// </summary>
    /// <summary>
    /// Reuses <paramref name="current"/> verbatim (same reference) when its content
    /// already matches what would be rebuilt — critical, not just an optimization: picking
    /// an item from an already-fully-shown dropdown (e.g. clicking a post-midnight End
    /// option) changes Text to another value that ALSO resolves to "show the full list,"
    /// so before/after content is identical. Swapping in a brand-new collection instance
    /// there would still replace ItemsSource synchronously while AutoCompleteBox is mid-way
    /// through handling that very click's own selection-changed logic — confirmed via a
    /// real repro (ArgumentOutOfRangeException deep inside AutoCompleteBox.ClearView(),
    /// re-entered from its own SelectionModel callback). Reusing the same instance means
    /// the generated property setter's equality check (same reference) skips raising
    /// PropertyChanged entirely, so ItemsSource is never touched during that click at all.
    /// </summary>
    private static (ObservableCollection<T> Items, int? ScrollIndex) BuildVisibleTimeOptions<T>(
        ObservableCollection<T> current, List<T> full, string text, Func<T, string> textOf)
    {
        if (TryParseFormTime(text, out var parsed))
        {
            var formatted = $"{(int)parsed.TotalHours:D2}:{parsed.Minutes:D2}";
            var index = full.FindIndex(item => textOf(item) == formatted);
            var items = current.SequenceEqual(full) ? current : new ObservableCollection<T>(full);
            return (items, index >= 0 ? index : null);
        }

        var filtered = full.Where(item => textOf(item).StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
        var filteredItems = current.SequenceEqual(filtered) ? current : new ObservableCollection<T>(filtered);
        return (filteredItems, null);
    }

    private void UpdateVisibleStartTimeOptions(string text) =>
        (VisibleStartTimeOptions, StartTimeScrollIndex) = BuildVisibleTimeOptions(VisibleStartTimeOptions, TimeOptions.ToList(), text, t => t);

    private void UpdateVisibleEndTimeOptions(string text) =>
        (VisibleEndTimeOptions, EndTimeScrollIndex) = BuildVisibleTimeOptions(VisibleEndTimeOptions, EndTimeOptions.ToList(), text, o => o.TimeText);

    private void UpdateVisibleSplitFirstStartTimeOptions(string text) =>
        (VisibleSplitFirstStartTimeOptions, SplitFirstStartTimeScrollIndex) = BuildVisibleTimeOptions(VisibleSplitFirstStartTimeOptions, TimeOptions.ToList(), text, t => t);

    private void UpdateVisibleSplitFirstEndTimeOptions(string text) =>
        (VisibleSplitFirstEndTimeOptions, SplitFirstEndTimeScrollIndex) = BuildVisibleTimeOptions(VisibleSplitFirstEndTimeOptions, TimeOptions.ToList(), text, t => t);

    private void UpdateVisibleSplitSecondStartTimeOptions(string text) =>
        (VisibleSplitSecondStartTimeOptions, SplitSecondStartTimeScrollIndex) = BuildVisibleTimeOptions(VisibleSplitSecondStartTimeOptions, TimeOptions.ToList(), text, t => t);

    private void UpdateVisibleSplitSecondEndTimeOptions(string text) =>
        (VisibleSplitSecondEndTimeOptions, SplitSecondEndTimeScrollIndex) = BuildVisibleTimeOptions(VisibleSplitSecondEndTimeOptions, TimeOptions.ToList(), text, t => t);

    /// <summary>Wired from the View's code-behind on the Start field's LostFocus — an
    /// invalid value snaps back to the last one that successfully parsed.</summary>
    public void OnFormStartTimeLostFocus()
    {
        if (IsFormStartTimeInvalid) FormStartTime = _lastValidFormStartTime;
    }

    /// <summary>Wired from the View's code-behind on the End field's LostFocus — an
    /// invalid value snaps back to the last one that successfully parsed.</summary>
    public void OnFormEndTimeLostFocus()
    {
        if (IsFormEndTimeInvalid) FormEndTime = _lastValidFormEndTime;
    }

    private void UpdateEndRolloverDisplay(TimeSpan start, TimeSpan end)
    {
        FormEndRollsToNextDay = PlannerTimeShiftMath.EndRollsToNextDay(start, end);
        FormEndDateText = FormEndRollsToNextDay
            ? FormDate.DateTime.AddDays(1).ToString("MMM d, yyyy")
            : string.Empty;
    }

    /// <summary>
    /// Rebuilds the End dropdown from the current Start value and block type. While
    /// drafting a contract, entries that would roll to the next day are excluded — draft
    /// blocks keep today's same-day-only behavior (see the shift-math linkage's identical
    /// exclusion above).
    /// </summary>
    private void RegenerateEndTimeOptions()
    {
        EndTimeOptions.Clear();
        if (!TryParseFormTime(FormStartTime, out var start)) return;

        foreach (var offset in PlannerEndTimeOptions.GenerateOffsets(IsFocusBlock))
        {
            var end = PlannerTimeShiftMath.ShiftEnd(start, offset);
            if (IsDraftingContract && PlannerTimeShiftMath.EndRollsToNextDay(start, end))
                break;

            EndTimeOptions.Add(new PlannerEndTimeOptionDto($"{(int)end.TotalHours:D2}:{end.Minutes:D2}", offset));
        }
    }

    /// <summary>
    /// Sets FormStartTime/FormEndTime together and (re)initializes the duration-preserving
    /// shift state from them — the single entry point every form-population site should
    /// use instead of assigning the two properties directly, so opening the form never
    /// fires the shift handlers against stale leftover values from a previous session.
    /// </summary>
    private void SetFormTimes(TimeSpan start, TimeSpan end)
    {
        _isPopulatingFormTimes = true;
        FormStartTime = $"{(int)start.TotalHours:D2}:{start.Minutes:D2}";
        FormEndTime = $"{(int)end.TotalHours:D2}:{end.Minutes:D2}";
        _isPopulatingFormTimes = false;

        _preservedFormDuration = PlannerTimeShiftMath.ResolvedDuration(start, end);
        UpdateEndRolloverDisplay(start, end);
    }

    // Only offered when creating a brand-new block (not editing one, not drafting a
    // contract) — composes the same ComputeRedemptionAttributionQuery
    // CreatePlannedBlockCommandHandler itself calls, so this preview can never drift from
    // what Save will actually do. No toggle, no user decision: the block either qualifies
    // or it doesn't, and this is purely informational.
    private async Task RefreshRedemptionPreviewAsync()
    {
        RedemptionSuggestionNotice = null;

        if (!IsFocusBlock || SelectedPreset is null || IsEditing || IsDraftingContract)
            return;
        if (!TryParseFormTime(FormStartTime, out var startTime) || !TryParseFormTime(FormEndTime, out var endTime))
            return;

        var (startUtc, endUtc) = ResolveFormRangeUtc(startTime, endTime);

        var attribution = await _computeRedemptionAttribution.HandleAsync(
            new ComputeRedemptionAttributionQuery(null, SelectedPreset.Id, startUtc, endUtc, ExcludeBlockId: null));

        if (attribution.Entries.Count == 0) return;

        var totalCovered = attribution.Entries.Sum(e => e.MinutesCovered);
        RedemptionSuggestionNotice = attribution.Entries.Count == 1
            ? $"This will cover {totalCovered:0.#} min of your missed {SelectedPreset.Name} block."
            : $"This will cover {totalCovered:0.#} min across {attribution.Entries.Count} missed {SelectedPreset.Name} blocks.";
    }

    /// <summary>
    /// Resolves FormDate + a Start/End time-of-day pair into actual UTC instants, rolling
    /// End onto the day after FormDate when it's at or before Start — the non-draft form's
    /// single source of truth for "what does this range actually mean," shared by the
    /// redemption preview and Save so they can never resolve it differently.
    /// </summary>
    private (DateTime StartUtc, DateTime EndUtc) ResolveFormRangeUtc(TimeSpan startTime, TimeSpan endTime)
    {
        var dayDate = FormDate.Date;
        var endDate = PlannerTimeShiftMath.EndRollsToNextDay(startTime, endTime) ? dayDate.AddDays(1) : dayDate;
        var startUtc = DateTime.SpecifyKind(dayDate.Add(startTime), DateTimeKind.Local).ToUniversalTime();
        var endUtc = DateTime.SpecifyKind(endDate.Add(endTime), DateTimeKind.Local).ToUniversalTime();
        return (startUtc, endUtc);
    }

    private async Task LoadWeekAsync()
    {
        IsLoading = true;
        Tiles.Clear();
        _blocksInWeek.Clear();

        for (int i = 0; i < 7; i++)
        {
            var day = WeekStart.AddDays(i);
            var query = new GetPlannedBlocksForDayQuery { Date = DateOnly.FromDateTime(day) };
            var blocks = await _getBlocksForDay.HandleAsync(query);

            foreach (var b in blocks)
            {
                _blocksInWeek[b.Id] = b;
                var tile = BuildTile(b, i);
                if (tile is not null) Tiles.Add(tile);
            }
        }

        if (IsDraftingContract)
        {
            AppendDraftTiles();
            await RefreshDayFloorPreviewsAsync();
        }

        IsLoading = false;
    }

    private PlannedBlockTileViewModel? BuildTile(PlannedBlockDto dto, int columnIndex)
    {
        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();

        var dayStart = WeekStart.AddDays(columnIndex);
        var visibleStart = dayStart.AddHours(FirstHour);
        var visibleEnd = dayStart.AddHours(LastHour);

        var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
        if (position is null) return null;

        var localDto = dto with { Start = localStart, End = localEnd };
        return new PlannedBlockTileViewModel(localDto, dto.BlockPresetType, columnIndex, position.Value.Top, position.Value.Height);
    }

    [RelayCommand]
    private async Task NewBlockAtAsync(GridSlotPosition slot)
    {
        await EnsurePresetsLoadedAsync();
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        IsStartLocked = false;
        FormTitle = "New Block";
        ResetForm();

        var blockDate = DateTime.SpecifyKind(WeekStart.AddDays(slot.ColumnIndex).Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        SetFormTimes(TimeSpan.FromHours(slot.Hour), TimeSpan.FromHours(slot.Hour + 1));
        SelectedPreset = AvailablePresets.FirstOrDefault();
        IsFormVisible = true;
    }

    [RelayCommand]
    private async Task EditBlockAsync(PlannedBlockTileViewModel tile)
    {
        if (tile.IsDraft)
        {
            var draft = _draftBlocks.FirstOrDefault(d => d.Id == tile.Id);
            if (draft is null) return;
            await EnsurePresetsLoadedAsync();
            _editingBlockId = draft.Id;
            _editingIsDraft = true;
            IsEditing = true;
            IsStartLocked = false;
            IsEditingCompleted = false;
            IsEditingBlockEnded = false;
            IsEditingBlockContractLocked = false;
            IsEditingBlockDailyFlexibility = false;
            FormTitle = "Edit Draft Block";
            ResetForm();

            var draftLocalStart = draft.Start.ToLocalTime();
            var draftLocalEnd = draft.End.ToLocalTime();
            var draftBlockDate = DateTime.SpecifyKind(draftLocalStart.Date, DateTimeKind.Utc);
            FormDate = new DateTimeOffset(draftBlockDate, TimeSpan.Zero);

            SetFormTimes(draftLocalStart.TimeOfDay, draftLocalEnd.TimeOfDay);
            IsFocusBlock = draft.IsFocus;
            SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Id == draft.PresetId)
                              ?? AvailablePresets.FirstOrDefault();
            IsFormVisible = true;
            return;
        }

        if (_sessionState.ActiveSession?.OriginatingBlockId == tile.Id) return;
        if (!_blocksInWeek.TryGetValue(tile.Id, out var dto)) return;

        // Done blocks (Focus: Outcome resolved; Background: no Outcome concept, HasEnded is
        // the only "done" signal) get the read-only snapshot instead of the edit form — the
        // active-session guard above already took priority, so nothing here is still running.
        var isDone = dto.BlockPresetType == BlockPresetType.Focus ? dto.Outcome.HasValue : dto.HasEnded;
        if (isDone)
        {
            await ShowSnapshotAsync(dto);
            return;
        }

        await EnsurePresetsLoadedAsync();
        _editingBlockId = dto.Id;
        _editingIsDraft = false;
        IsEditing = true;
        IsStartLocked = dto.Start <= _clockService.LocalNow.ToUniversalTime();
        IsEditingCompleted = dto.IsCompleted;
        IsEditingBlockEnded = dto.HasEnded;
        IsEditingBlockContractLocked = dto.IsUnderInEffectContract;
        IsEditingBlockDailyFlexibility = dto.ContractStrictnessRule == StrictnessRule.DailyFlexibility;
        FormTitle = "Edit Block";
        ResetForm();

        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();
        var blockDate = DateTime.SpecifyKind(localStart.Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        SetFormTimes(localStart.TimeOfDay, localEnd.TimeOfDay);
        IsFocusBlock = dto.BlockPresetType == BlockPresetType.Focus;
        SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Id == dto.PresetId)
                          ?? AvailablePresets.FirstOrDefault();
        IsFormVisible = true;
    }

    private async Task ShowSnapshotAsync(PlannedBlockDto dto)
    {
        SnapshotBlock = dto;
        // Background blocks have no Ritual, so no Session is ever tied to one — skip the
        // query entirely rather than asking it a question that's always null by construction.
        SnapshotSession = dto.BlockPresetType == BlockPresetType.Focus
            ? await _getSessionSnapshot.HandleAsync(new GetSessionSnapshotForBlockQuery { PlannedBlockId = dto.Id })
            : null;
        DeleteSnapshotErrorMessage = null;
        IsSnapshotVisible = true;
    }

    [RelayCommand]
    private void CloseSnapshot()
    {
        IsSnapshotVisible = false;
        SnapshotBlock = null;
        SnapshotSession = null;
    }

    [RelayCommand]
    private void RequestDeleteSnapshotBlock()
    {
        DeleteSnapshotErrorMessage = null;
        IsConfirmDeleteSnapshotVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSnapshotBlockAsync()
    {
        IsConfirmDeleteSnapshotVisible = false;
        if (SnapshotBlock is not { } block) return;

        var result = await _deleteBlock.HandleAsync(new DeletePlannedBlockCommand { BlockId = block.Id });
        if (!result.Success)
        {
            DeleteSnapshotErrorMessage = result.ErrorMessage;
            return;
        }

        CloseSnapshot();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private void CancelDeleteSnapshotBlock() => IsConfirmDeleteSnapshotVisible = false;

    partial void OnSnapshotBlockChanged(PlannedBlockDto? value)
    {
        OnPropertyChanged(nameof(CanDeleteSnapshotBlock));
        OnPropertyChanged(nameof(ShowSnapshotNoSessionMessage));
    }

    partial void OnSnapshotSessionChanged(SessionSnapshotDto? value)
    {
        OnPropertyChanged(nameof(HasSnapshotSession));
        OnPropertyChanged(nameof(ShowSnapshotNoSessionMessage));
    }

    private async Task EnsurePresetsLoadedAsync()
    {
        if (AvailablePresets.Count == 0) await LoadPresetsAsync();
    }

    private void ResetForm()
    {
        FormError = null;
        IsFocusBlock = true;
        SelectedPreset = null;
    }

    [RelayCommand] private void SetFocusBlock() => IsFocusBlock = true;
    [RelayCommand] private void SetBackgroundBlock() => IsFocusBlock = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsDraftingContract)
        {
            if (!SaveDraftBlock()) return;
            await RefreshDayFloorPreviewsAsync();
            CloseForm();
            return;
        }

        FormError = null;
        if (SelectedPreset is null) { FormError = "Please select a block preset."; return; }
        if (!TryParseFormTime(FormStartTime, out var startTime) || !TryParseFormTime(FormEndTime, out var endTime))
        {
            FormError = "Invalid time format. Use HH:mm.";
            return;
        }
        var (startUtc, endUtc) = ResolveFormRangeUtc(startTime, endTime);

        var blockPresetType = IsFocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;

        if (IsEditing && _editingBlockId.HasValue)
        {
            var typeChanged = _blocksInWeek.TryGetValue(_editingBlockId.Value, out var originalDto)
                && originalDto.BlockPresetType != blockPresetType;

            if (typeChanged)
            {
                var deleteResult = await _deleteBlock.HandleAsync(
                    new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
                if (!deleteResult.Success) { FormError = deleteResult.ErrorMessage; return; }

                var createCmd = new CreatePlannedBlockCommand
                {
                    PresetId = SelectedPreset.Id,
                    BlockPresetType = blockPresetType,
                    Start = startUtc,
                    End = endUtc,
                    ContractId = originalDto!.ContractId
                };
                var createResult = await _createBlock.HandleAsync(createCmd);
                if (!createResult.Success) { FormError = createResult.ErrorMessage; return; }
            }
            else
            {
                var result = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
                {
                    BlockId = _editingBlockId.Value,
                    PresetId = SelectedPreset.Id,
                    Start = startUtc,
                    End = endUtc
                });
                if (!result.Success) { FormError = result.ErrorMessage; return; }
            }
        }
        else
        {
            // No ContractId, no RedemptionEntries — CreatePlannedBlockCommandHandler
            // auto-discovers whether this block should attach to a contract and cover a
            // deficit, via the exact same query RefreshRedemptionPreviewAsync already
            // showed a preview from.
            var result = await _createBlock.HandleAsync(new CreatePlannedBlockCommand
            {
                PresetId = SelectedPreset.Id,
                BlockPresetType = blockPresetType,
                Start = startUtc,
                End = endUtc
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
        CloseForm();
    }

    // Validates and commits a draft block into the in-memory _draftBlocks list — no
    // repository call, nothing persists until the contract is sealed. Returns false (and
    // leaves FormError set) on a rejected placement — callers must not close the form or
    // clear FormError in that case, or the rejection message is wiped before it's seen.
    private bool SaveDraftBlock()
    {
        FormError = null;
        if (SelectedPreset is null) { FormError = "Please select a block preset."; return false; }
        if (!TryParseFormTime(FormStartTime, out var startTime) || !TryParseFormTime(FormEndTime, out var endTime))
        {
            FormError = "Invalid time format. Use HH:mm.";
            return false;
        }
        if (endTime <= startTime) { FormError = "End time must be after start time."; return false; }

        var dayDate = FormDate.Date;
        var localStart = DateTime.SpecifyKind(dayDate.Add(startTime), DateTimeKind.Local);
        var localEnd = DateTime.SpecifyKind(dayDate.Add(endTime), DateTimeKind.Local);

        bool isFocus = IsFocusBlock;
        var placementError = ValidateDraftPlacement(localStart, localEnd, isFocus);
        if (placementError is not null) { FormError = placementError; return false; }

        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        var columnIndex = (dayDate - WeekStart.Date).Days;
        if (columnIndex is >= 0 and <= 6)
        {
            var excludeId = _editingIsDraft ? _editingBlockId ?? Guid.Empty : Guid.Empty;
            var dayTiles = Tiles.Where(t => t.ColumnIndex == columnIndex);
            if (PlannerDragMath.WouldCollide(excludeId, isFocus, startUtc, endUtc, dayTiles))
            {
                FormError = "Focus blocks cannot overlap — choose a different time.";
                return false;
            }
        }

        if (_editingIsDraft && _editingBlockId.HasValue)
        {
            var idx = _draftBlocks.FindIndex(d => d.Id == _editingBlockId.Value);
            if (idx >= 0)
                _draftBlocks[idx] = new ContractDraftBlock(_editingBlockId.Value, SelectedPreset.Id, SelectedPreset.Name, startUtc, endUtc, isFocus);
        }
        else
        {
            _draftBlocks.Add(new ContractDraftBlock(Guid.NewGuid(), SelectedPreset.Id, SelectedPreset.Name, startUtc, endUtc, isFocus));
        }

        RebuildDraftTilesInPlace();
        return true;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_editingBlockId.HasValue) return;

        if (_editingIsDraft)
        {
            _draftBlocks.RemoveAll(d => d.Id == _editingBlockId.Value);
            RebuildDraftTilesInPlace();
            await RefreshDayFloorPreviewsAsync();
            CloseForm();
            return;
        }

        var result = await _deleteBlock.HandleAsync(new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
        if (result.Success) { await LoadWeekAsync(); CloseForm(); }
        else { FormError = result.ErrorMessage; }
    }

    // ── Split Block ────────────────────────────────────────────────────────────

    // Pre-fills a 50/50 split of the block currently open in the edit form — the two
    // resulting ranges are placed back-to-back; repositioning either half afterward is a
    // separate, ordinary drag/move operation, not part of this command.
    [RelayCommand]
    private void OpenSplitDialog()
    {
        if (!CanSplitBlock || !_editingBlockId.HasValue) return;
        if (!_blocksInWeek.TryGetValue(_editingBlockId.Value, out var dto)) return;

        SplitError = null;
        var localStart = dto.Start.ToLocalTime();
        var localEnd = dto.End.ToLocalTime();
        var midpoint = localStart + TimeSpan.FromTicks((localEnd - localStart).Ticks / 2);

        _isPopulatingSplitTimes = true;
        SplitFirstStartTime = $"{localStart.Hour:D2}:{localStart.Minute:D2}";
        SplitFirstEndTime = $"{midpoint.Hour:D2}:{midpoint.Minute:D2}";
        SplitSecondStartTime = $"{midpoint.Hour:D2}:{midpoint.Minute:D2}";
        SplitSecondEndTime = $"{localEnd.Hour:D2}:{localEnd.Minute:D2}";
        _isPopulatingSplitTimes = false;

        _preservedSplitFirstDuration = midpoint - localStart;
        _preservedSplitSecondDuration = localEnd - midpoint;

        IsSplitDialogVisible = true;
    }

    partial void OnSplitFirstStartTimeChanged(string value)
    {
        UpdateVisibleSplitFirstStartTimeOptions(value);
        if (_isPopulatingSplitTimes || !TryParseFormTime(value, out var newStart)) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedSplitFirstDuration);
        _isPopulatingSplitTimes = true;
        SplitFirstEndTime = $"{(int)newEnd.TotalHours:D2}:{newEnd.Minutes:D2}";
        _isPopulatingSplitTimes = false;
    }

    partial void OnSplitFirstEndTimeChanged(string value)
    {
        UpdateVisibleSplitFirstEndTimeOptions(value);
        if (_isPopulatingSplitTimes) return;
        if (!TryParseFormTime(SplitFirstStartTime, out var start) || !TryParseFormTime(value, out var newEnd)) return;

        _preservedSplitFirstDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
    }

    partial void OnSplitSecondStartTimeChanged(string value)
    {
        UpdateVisibleSplitSecondStartTimeOptions(value);
        if (_isPopulatingSplitTimes || !TryParseFormTime(value, out var newStart)) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedSplitSecondDuration);
        _isPopulatingSplitTimes = true;
        SplitSecondEndTime = $"{(int)newEnd.TotalHours:D2}:{newEnd.Minutes:D2}";
        _isPopulatingSplitTimes = false;
    }

    partial void OnSplitSecondEndTimeChanged(string value)
    {
        UpdateVisibleSplitSecondEndTimeOptions(value);
        if (_isPopulatingSplitTimes) return;
        if (!TryParseFormTime(SplitSecondStartTime, out var start) || !TryParseFormTime(value, out var newEnd)) return;

        _preservedSplitSecondDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
    }

    [RelayCommand]
    private void CloseSplitDialog()
    {
        IsSplitDialogVisible = false;
        SplitError = null;
    }

    [RelayCommand]
    private async Task ConfirmSplitAsync()
    {
        if (!_editingBlockId.HasValue) return;
        SplitError = null;

        if (!TryParseFormTime(SplitFirstStartTime, out var firstStart) || !TryParseFormTime(SplitFirstEndTime, out var firstEnd)
            || !TryParseFormTime(SplitSecondStartTime, out var secondStart) || !TryParseFormTime(SplitSecondEndTime, out var secondEnd))
        {
            SplitError = "Invalid time format. Use HH:mm.";
            return;
        }

        var dayDate = FormDate.Date;
        var firstStartUtc = DateTime.SpecifyKind(dayDate.Add(firstStart), DateTimeKind.Local).ToUniversalTime();
        var firstEndUtc = DateTime.SpecifyKind(dayDate.Add(firstEnd), DateTimeKind.Local).ToUniversalTime();
        var secondStartUtc = DateTime.SpecifyKind(dayDate.Add(secondStart), DateTimeKind.Local).ToUniversalTime();
        var secondEndUtc = DateTime.SpecifyKind(dayDate.Add(secondEnd), DateTimeKind.Local).ToUniversalTime();

        var result = await _splitBlock.HandleAsync(new SplitPlannedBlockCommand
        {
            OriginalBlockId = _editingBlockId.Value,
            FirstBlockStart = firstStartUtc,
            FirstBlockEnd = firstEndUtc,
            SecondBlockStart = secondStartUtc,
            SecondBlockEnd = secondEndUtc
        });

        if (!result.Success)
        {
            SplitError = result.ErrorMessage;
            return;
        }

        IsSplitDialogVisible = false;
        await LoadWeekAsync();
        await RefreshActiveContractAsync(); // badge refresh — same mechanism drag-end and form-save now also use
        CloseForm();
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormVisible = false;
        FormError = null;
        IsStartLocked = false;
        IsEditingCompleted = false;
        IsEditingBlockEnded = false;
        IsEditingBlockContractLocked = false;
        IsEditingBlockDailyFlexibility = false;
        IsSplitDialogVisible = false;
        SplitError = null;
        RedemptionSuggestionNotice = null;
        _editingBlockId = null;
        _editingIsDraft = false;
    }

    // ── Time parsing ─────────────────────────────────────────────────────────────
    // TimeSpan.TryParse rejects hours > 23, but "24:00" is a valid end-of-day value.
    // Internal (not private) so it's directly unit-testable — this exact patch closes a
    // real latent bug: without the Days check, typing e.g. "93" parses "successfully" as a
    // 93-day TimeSpan (TimeSpan.Parse rolls anything over 23 hours into extra days rather
    // than rejecting it), silently placing a block 93 days out with nothing to catch it.
    internal static bool TryParseFormTime(string s, out TimeSpan result)
    {
        if (s == "24:00") { result = TimeSpan.FromHours(24); return true; }
        if (!TimeSpan.TryParse(s, out result)) return false;
        if (result.Days != 0) { result = default; return false; }
        return true;
    }

    // ── Drag lock checks (called from code-behind before starting a drag) ──────

    /// <summary>
    /// True when the tile must not be dragged at all — it is completed, has already
    /// ended (covers Missed, EndedEarly, and any other resolved-in-the-past block), or
    /// is the active session's originating block. Draft blocks are never locked.
    /// </summary>
    public bool IsTileFullyLocked(Guid blockId)
    {
        if (_draftBlocks.Any(d => d.Id == blockId)) return false;
        if (!_blocksInWeek.TryGetValue(blockId, out var dto)) return true;
        return dto.IsCompleted || dto.HasEnded || _sessionState.ActiveSession?.OriginatingBlockId == blockId;
    }

    /// <summary>
    /// True when the block's start time is in the past — move and top-resize
    /// are disallowed; bottom-resize on a past-start block is also disallowed.
    /// Draft blocks are never start-locked.
    /// </summary>
    public bool IsTileStartLocked(Guid blockId)
    {
        if (_draftBlocks.Any(d => d.Id == blockId)) return false;
        if (!_blocksInWeek.TryGetValue(blockId, out var dto)) return true;
        return dto.Start <= _clockService.LocalNow.ToUniversalTime();
    }

    // ── Drag-create: open the form pre-filled with the dragged range ──────────

    [RelayCommand]
    private async Task OpenNewBlockFormWithRangeAsync(DragCreatedRange range)
    {
        await EnsurePresetsLoadedAsync();
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        IsStartLocked = false;
        FormTitle = "New Block";
        ResetForm();

        var blockDate = DateTime.SpecifyKind(WeekStart.AddDays(range.ColumnIndex).Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);

        SetFormTimes(range.LocalStart, range.LocalEnd);
        SelectedPreset = AvailablePresets.FirstOrDefault();
        IsFormVisible = true;
    }

    // ── Drag-move / drag-resize: commit via UpdatePlannedBlockCommand ─────────

    [RelayCommand]
    private async Task MoveOrResizeBlockAsync(DragMoveResult result)
    {
        DragError = null;

        var draftIndex = _draftBlocks.FindIndex(d => d.Id == result.BlockId);
        if (draftIndex >= 0)
        {
            var placementError = ValidateDraftPlacement(
                result.NewStartUtc.ToLocalTime(), result.NewEndUtc.ToLocalTime(), _draftBlocks[draftIndex].IsFocus);
            if (placementError is not null)
            {
                // No rebuild needed: _draftBlocks is untouched, and the code-behind's
                // ClearDragState() already removed the drag ghost and restored the real
                // tile's opacity before this command ran — Tiles already reflects the
                // unchanged, correct position.
                DragError = placementError;
                return;
            }

            _draftBlocks[draftIndex] = _draftBlocks[draftIndex] with { Start = result.NewStartUtc, End = result.NewEndUtc };
            RebuildDraftTilesInPlace();
            await RefreshDayFloorPreviewsAsync();
            return;
        }

        if (!_blocksInWeek.TryGetValue(result.BlockId, out var dto))
        {
            DragError = "Block not found — please refresh.";
            return;
        }

        if (IsTileFullyLocked(result.BlockId))
        {
            DragError = "This block is locked and cannot be moved.";
            return;
        }

        var updateResult = await _updateBlock.HandleAsync(new UpdatePlannedBlockCommand
        {
            BlockId   = result.BlockId,
            PresetId = dto.PresetId,
            Start     = result.NewStartUtc,
            End       = result.NewEndUtc
        });

        if (!updateResult.Success)
            DragError = updateResult.ErrorMessage;

        // Reload either way: on success to reflect the committed change, on failure so the
        // tile snaps back to its actual (unchanged) position — the drag gesture already
        // moved it visually, and without this it would stay stuck at the rejected drop
        // point until some unrelated reload happened to fire.
        await LoadWeekAsync();
        // Badge/banner refresh — previously only caught up on the next 3s poll tick,
        // which read as a laggy update right after the drag that actually caused it.
        await RefreshActiveContractAsync();
    }

    // ── Nen Contract wizard + drafting mode ───────────────────────────────────

    [RelayCommand]
    private void OpenContractWizard()
    {
        if (!CanOpenContractWizard) return;
        _editingContractPayload = null;
        IsNenContractPopoverVisible = false;
        WizardViewModel = new ContractWizardViewModel(
            getPresets: _getContractPresets,
            checkOverlap: _checkOverlap,
            initialStartDate: DateOnly.FromDateTime(_clockService.LocalNow.Date),
            onReadyToSchedule: BeginDrafting,
            onCancel: CloseWizard);
        IsWizardVisible = true;
        ContractWizardOpened?.Invoke(WizardViewModel);
    }

    // Popover's Edit action on an Upcoming (Scheduled) contract. Gated the same way as
    // OpenContractWizard (overlay collision only, not session-active — see CanOpenContractWizard).
    [RelayCommand]
    private async Task RequestEditContractAsync(Guid contractId)
    {
        if (!CanOpenContractWizard) return;

        var payload = await _getContractForEdit.HandleAsync(new GetNenContractForEditQuery(contractId));
        if (payload is null) return;

        _editingContractPayload = payload;
        IsNenContractPopoverVisible = false;
        WizardViewModel = new ContractWizardViewModel(
            getPresets: _getContractPresets,
            checkOverlap: _checkOverlap,
            initialStartDate: payload.StartDate,
            onReadyToSchedule: BeginDrafting,
            onCancel: CloseWizard,
            editingContract: payload);
        IsWizardVisible = true;
        ContractWizardOpened?.Invoke(WizardViewModel);
    }

    private void CloseWizard()
    {
        IsWizardVisible = false;
        WizardViewModel = null;
        ContractWizardClosed?.Invoke();
    }

    private void BeginDrafting(NenContractDraftRules rules)
    {
        _draftRules = rules;
        _draftBlocks.Clear();

        // Editing an existing Scheduled contract: seed the calendar with its already-drafted
        // blocks instead of a blank week, so "edit" actually shows what's being edited.
        if (rules.EditingContractId.HasValue && _editingContractPayload is not null)
        {
            foreach (var block in _editingContractPayload.Blocks)
            {
                _draftBlocks.Add(new ContractDraftBlock(
                    Guid.NewGuid(), block.PresetId, block.PresetName, block.Start, block.End, block.IsFocus));
            }
        }

        CloseWizard();
        IsDraftingContract = true;
        DraftContractName = rules.Name;
        SealErrorMessage = null;

        WeekStart = StartOfWeek(rules.StartDate.ToDateTime(TimeOnly.MinValue));
        BuildDayHeaders();
        UpdateCurrentTimePosition();
        _ = LoadWeekAsync();
    }

    private void AppendDraftTiles()
    {
        foreach (var draft in _draftBlocks)
        {
            var localStart = draft.Start.ToLocalTime();
            var localEnd = draft.End.ToLocalTime();
            var dayIndex = (localStart.Date - WeekStart.Date).Days;
            if (dayIndex < 0 || dayIndex > 6) continue;

            var dayStart = WeekStart.AddDays(dayIndex);
            var visibleStart = dayStart.AddHours(FirstHour);
            var visibleEnd = dayStart.AddHours(LastHour);
            var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
            if (position is null) continue;

            var blockPresetType = draft.IsFocus ? BlockPresetType.Focus : BlockPresetType.Background;
            var syntheticDto = new PlannedBlockDto
            {
                Id = draft.Id,
                PresetId = draft.PresetId,
                PresetName = draft.PresetName,
                BlockPresetType = blockPresetType,
                Start = localStart,
                End = localEnd,
                IsCompleted = false,
                HasEnded = false
            };

            Tiles.Add(new PlannedBlockTileViewModel(
                syntheticDto, blockPresetType, dayIndex, position.Value.Top, position.Value.Height, isDraft: true));
        }
    }

    private void RebuildDraftTilesInPlace()
    {
        var nonDraftTiles = Tiles.Where(t => !t.IsDraft).ToList();
        Tiles.Clear();
        foreach (var t in nonDraftTiles) Tiles.Add(t);
        AppendDraftTiles();
    }

    // Shared by SaveDraftBlock (create/edit-via-form) and MoveOrResizeBlockAsync's draft
    // branch (drag-move/resize of an already-placed draft tile) — same rules, same
    // reject-after-the-fact mechanism every other Planner rule already uses (Focus-overlap,
    // Work Hours, past-date for ordinary blocks). Returns null when the placement is valid.
    // Work Hours mirrors NenContract.AllowsBlockPlacement (same WorkHours.ContainsRange
    // domain check the sealed-contract guard uses) so drafting and post-seal editing enforce
    // the identical rule — no NenContract exists yet during drafting to delegate to directly,
    // but the wizard's flat Default-only window (no per-day exceptions in the UI) is enough
    // to reconstruct it locally. Focus-only: Background blocks are never Work-Hours constrained.
    private string? ValidateDraftPlacement(DateTime localStart, DateTime localEnd, bool isFocus)
    {
        if (localStart <= _clockService.LocalNow)
            return "Blocks cannot start in the past.";

        if (_draftRules is not null)
        {
            var startDate = DateOnly.FromDateTime(localStart);
            var endDate = DateOnly.FromDateTime(localEnd);
            if (startDate < _draftRules.StartDate || endDate > _draftRules.EndDate)
                return $"Blocks must be placed within the contract's date range ({_draftRules.StartDate:MMM d} – {_draftRules.EndDate:MMM d}).";

            if (isFocus && _draftRules.WorkHoursStart is { } workHoursStart && _draftRules.WorkHoursEnd is { } workHoursEnd)
            {
                var workHours = new WorkHours(workHoursStart, workHoursEnd);
                if (!workHours.ContainsRange(localStart, localEnd))
                    return $"This block falls outside the contract's Work Hours ({workHoursStart:h:mm tt} – {workHoursEnd:h:mm tt}).";
            }
        }

        return null;
    }

    private async Task RefreshDayFloorPreviewsAsync()
    {
        if (_draftRules is null) { DayFloorByDate = new(); return; }

        var focusRanges = _draftBlocks
            .Where(d => d.IsFocus)
            .Select(d => (d.Start, d.End))
            .ToList();

        var hoursByDay = await _computePlannedFocusHours.HandleAsync(
            new ComputePlannedFocusHoursQuery(ContractId: null, DraftFocusBlockRanges: focusRanges));

        var byDate = new Dictionary<DateOnly, DayFloorPreviewViewModel>();
        for (int i = 0; i < 7; i++)
        {
            var date = DateOnly.FromDateTime(WeekStart.AddDays(i));
            if (date < _draftRules.StartDate || date > _draftRules.EndDate) continue;

            var scheduled = hoursByDay.GetValueOrDefault(date);
            bool isRestDay = _draftRules.FixedDailyFloorHours.HasValue
                ? _draftRules.RestDays.Contains(date)
                : scheduled <= 0;
            double? requiredFloor = _draftRules.FixedDailyFloorHours.HasValue && !isRestDay
                ? _draftRules.FixedDailyFloorHours
                : null;

            byDate[date] = new DayFloorPreviewViewModel(date, scheduled, requiredFloor, isRestDay);
        }

        DayFloorByDate = byDate;
    }

    [RelayCommand]
    private async Task SealContractAsync()
    {
        if (_draftRules is null) return;
        SealErrorMessage = null;
        IsSealing = true;
        try
        {
            var blocks = _draftBlocks
                .Select(d => new NenContractBlockSpec(d.PresetId, d.Start, d.End, d.IsFocus))
                .ToList();

            bool success;
            string? errorMessage;

            if (_draftRules.EditingContractId is { } editingContractId)
            {
                var editResult = await _editContract.HandleAsync(new EditNenContractCommand
                {
                    ContractId = editingContractId,
                    Name = _draftRules.Name,
                    Goal = _draftRules.Goal,
                    StartDate = _draftRules.StartDate,
                    EndDate = _draftRules.EndDate,
                    StrictnessRule = _draftRules.StrictnessRule,
                    AllowCancellation = _draftRules.AllowCancellation,
                    FixedDailyFloorHours = _draftRules.FixedDailyFloorHours,
                    RestDays = _draftRules.RestDays,
                    WorkHoursStart = _draftRules.WorkHoursStart,
                    WorkHoursEnd = _draftRules.WorkHoursEnd,
                    Blocks = blocks,
                    ContractPresetId = _draftRules.ContractPresetId
                });
                success = editResult.Success;
                errorMessage = editResult.ErrorMessage;
            }
            else
            {
                var sealResult = await _sealContract.HandleAsync(new SealNenContractCommand
                {
                    Name = _draftRules.Name,
                    Goal = _draftRules.Goal,
                    StartDate = _draftRules.StartDate,
                    EndDate = _draftRules.EndDate,
                    StrictnessRule = _draftRules.StrictnessRule,
                    AllowCancellation = _draftRules.AllowCancellation,
                    FixedDailyFloorHours = _draftRules.FixedDailyFloorHours,
                    RestDays = _draftRules.RestDays,
                    WorkHoursStart = _draftRules.WorkHoursStart,
                    WorkHoursEnd = _draftRules.WorkHoursEnd,
                    Blocks = blocks,
                    ContractPresetId = _draftRules.ContractPresetId
                });
                success = sealResult.Success;
                errorMessage = sealResult.ErrorMessage;
            }

            if (!success)
            {
                SealErrorMessage = errorMessage;
                return;
            }

            ExitDraftingMode();
            await LoadWeekAsync();
            await RefreshActiveContractAsync();
            await RefreshScheduledContractsAsync();
        }
        finally
        {
            IsSealing = false;
        }
    }

    [RelayCommand]
    private void RequestCancelDraft()
    {
        ConfirmDiscardDraftMessage = _draftBlocks.Count switch
        {
            0 => "Discard this contract draft?",
            1 => "Discard this contract draft and its 1 block?",
            var n => $"Discard this contract draft and its {n} blocks?"
        };
        IsConfirmDiscardDraftVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCancelDraftAsync()
    {
        IsConfirmDiscardDraftVisible = false;
        ExitDraftingMode();
        await LoadWeekAsync();
        await RefreshActiveContractAsync();
    }

    [RelayCommand]
    private void CancelCancelDraft() => IsConfirmDiscardDraftVisible = false;

    private void ExitDraftingMode()
    {
        IsDraftingContract = false;
        DraftContractName = null;
        _draftRules = null;
        _draftBlocks.Clear();
        DayFloorByDate = new();
        SealErrorMessage = null;
        _editingContractPayload = null;
    }

    // ── Nen Contract popover ────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleNenContractPopoverAsync()
    {
        if (IsNenContractPopoverVisible)
        {
            IsNenContractPopoverVisible = false;
            return;
        }

        await RefreshScheduledContractsAsync();
        IsNenContractPopoverVisible = true;
    }

    [RelayCommand]
    private void CloseNenContractPopover() => IsNenContractPopoverVisible = false;

    private async Task RefreshScheduledContractsAsync()
    {
        var scheduled = await _getScheduledContracts.HandleAsync(new GetScheduledNenContractsQuery());
        ScheduledContracts.Clear();
        foreach (var contract in scheduled)
            ScheduledContracts.Add(contract);
        OnPropertyChanged(nameof(HasNoContracts));
    }

    [RelayCommand]
    private void RequestDeleteContract(Guid contractId)
    {
        _contractIdPendingDelete = contractId;
        DeleteContractErrorMessage = null;
        ConfirmDeleteContractMessage = "Delete this contract? This cannot be undone.";
        IsConfirmDeleteContractVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteContractAsync()
    {
        if (_contractIdPendingDelete is not { } contractId) { IsConfirmDeleteContractVisible = false; return; }

        var result = await _deleteContract.HandleAsync(new DeleteNenContractCommand { ContractId = contractId });
        IsConfirmDeleteContractVisible = false;
        _contractIdPendingDelete = null;

        if (!result.Success)
        {
            DeleteContractErrorMessage = result.ErrorMessage;
            return;
        }

        await RefreshScheduledContractsAsync();
    }

    [RelayCommand]
    private void CancelDeleteContract() => IsConfirmDeleteContractVisible = false;

    public void Dispose()
    {
        _clockService.Tick -= OnClockTick;
        _sessionState.SessionChanged -= OnSessionChanged;
        _sessionState.PostSessionAcknowledged -= OnPostSessionAcknowledged;
        _floorStatusPollTimer.Elapsed -= OnFloorStatusPollTick;
        _floorStatusPollTimer.Dispose();
    }
}
