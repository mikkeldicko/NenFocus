using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetNenContractsForDateRange;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Application.Queries.GetContractWorkHoursForDate;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.Queries.GetInEffectNenContracts;
using NenFocus.Application.Queries.GetSessionSnapshotForBlock;
using NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;
using NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Application.UseCases.NenContracts.CompleteNenContract;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Converters;
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

    // Shared by every path that can trigger FocusBlock.CapEndToRedemptionCoverage — create
    // (via the confirm gate), edit (form Save), and drag-resize.
    private const string RedemptionCapMessage =
        "A redemption block can't be longer than the deficit it covers — shortened to match.";

    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IPlannerClockService _clockService;
    private readonly ISessionStateService _sessionState;

    private readonly Dictionary<Guid, PlannedBlockDto> _blocksInWeek = new();
    private readonly List<BlockPresetDto> _allPresets = new();
    private readonly List<ContractDraftBlock> _draftBlocks = new();

    // Parallel to TimeOptions (same index, same order) — populated once alongside it in
    // GenerateTimeOptions. Lets StartTimeScrollIndex's centering math work directly in
    // TimeSpan without re-parsing all 97 strings on every keystroke.
    private readonly List<TimeSpan> _timeOptionSpans = new();
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
    public ObservableCollection<PlannerEndTimeOption> EndTimeOptions { get; } = new();

    // Start and End both use a custom TextBox + Popup + ItemsControl (not AutoCompleteBox) —
    // TimeOptions/EndTimeOptions are always bound in full (see above); typing/arrow-keys only
    // ever change where the popup is scrolled/highlighted, never what's in it. Slicing
    // ItemsSource down by typing (the original model, still used below by the four Split
    // fields until they're migrated the same way) turned out to be the wrong model entirely,
    // not just a crash risk: confirmed via a real crash repro + the Debug > Windows > Call
    // Stack window that AutoCompleteBox's own SelectingItemsControlSelectionAdapter keeps
    // private keyboard-navigation state that can't be reached or reset from outside the
    // control, so swapping ItemsSource out while it holds a stale reference throws
    // ArgumentOutOfRangeException no matter how the swap is guarded from application code.
    //
    // IsStartTimeDropDownOpen/IsEndTimeDropDownOpen replace AutoCompleteBox's own
    // IsDropDownOpen, driven the same way (GotFocus/PointerPressed open it — see
    // PlannerPageView.axaml.cs) since the View still owns the actual Popup element.
    [ObservableProperty] private bool _isStartTimeDropDownOpen;
    [ObservableProperty] private bool _isEndTimeDropDownOpen;

    // Which TimeOptions/EndTimeOptions index the corresponding popup should be
    // centered/highlighted on right now — recomputed on every keystroke via ResolveApproxTime/
    // ResolveApproxEndDuration against the always-complete list. Bound directly from
    // PlannerPageView.axaml's own custom time-picker markup. [ObservableProperty] rather than
    // a plain property with a private setter specifically so the View's code-behind can react
    // to *this* property's own PropertyChanged rather than FormStartTime/FormEndTime's —
    // opening the dropdown must always reposition even when the newly-computed index happens
    // to equal what it already was (e.g. right after SetFormTimes populated the form), which
    // FormStartTime/FormEndTime's own change events can't distinguish from "nothing to do."
    [ObservableProperty] private int? _startTimeScrollIndex;
    [ObservableProperty] private int? _endTimeScrollIndex;

    // Split dialog's four time fields get the identical custom-picker treatment Start/End
    // already have, mirroring Start's own plain flat-list pattern (no duration labels —
    // these are ordinary times, not offsets). See OpenTimeDropdown/MoveTimeNext/
    // MoveTimePrevious below — the shared logic behind all four, called from thin per-field
    // wrapper methods so it isn't hand-duplicated four times over.
    [ObservableProperty] private bool _isSplitFirstStartTimeDropDownOpen;
    [ObservableProperty] private bool _isSplitFirstEndTimeDropDownOpen;
    [ObservableProperty] private bool _isSplitSecondStartTimeDropDownOpen;
    [ObservableProperty] private bool _isSplitSecondEndTimeDropDownOpen;
    [ObservableProperty] private int? _splitFirstStartTimeScrollIndex;
    [ObservableProperty] private int? _splitFirstEndTimeScrollIndex;
    [ObservableProperty] private int? _splitSecondStartTimeScrollIndex;
    [ObservableProperty] private int? _splitSecondEndTimeScrollIndex;
    // Per-day floor badge lookup, keyed by date — feeds the day-header badges. Populated
    // either by RefreshDayFloorPreviewsAsync (drafting) or RefreshSelectedContractAsync
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

    // Live syntax-only validation (can this text parse as a time-of-day at all) — always
    // kept current so other logic (shift-math guards, Save-time checks) can react to it
    // immediately. Independent of Save-time business-rule validation (12h cap, Work Hours,
    // floor, overlap), which is unchanged.
    [ObservableProperty] private bool _isFormStartTimeInvalid;
    [ObservableProperty] private bool _isFormEndTimeInvalid;

    // What the red-border styling actually binds to — deliberately NOT the same as
    // IsFormStartTimeInvalid/IsFormEndTimeInvalid above. Forced false while the field has
    // focus (OpenStartTimeDropdown/OpenEndTimeDropdown) so typing a partial value like "14"
    // never shows red mid-edit; only evaluated at LostFocus. A prior design silently
    // reverted an invalid value back to the last valid one on blur instead of showing red at
    // all — confirmed with the architect and deliberately dropped: silently rewriting what
    // the user typed without a clear signal was judged worse than leaving it exactly as
    // typed and pointing out the problem via the border.
    [ObservableProperty] private bool _showStartTimeInvalid;
    [ObservableProperty] private bool _showEndTimeInvalid;

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

    // ── Split Block ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSplitDialogVisible;
    [ObservableProperty] private string _splitFirstStartTime = "09:00";
    [ObservableProperty] private string _splitFirstEndTime = "10:00";
    [ObservableProperty] private string _splitSecondStartTime = "10:00";
    [ObservableProperty] private string _splitSecondEndTime = "11:00";
    [ObservableProperty] private string? _splitError;

    // First's End / Second's Start dropdowns show the duration each candidate time would
    // give that piece, mirroring the main form's own End field exactly (TimeText + Duration,
    // regenerated from that piece's own Start via PlannerEndTimeOptions.GenerateOffsets).
    // Deliberately NOT restricted to land inside the original block's own boundaries — the
    // architect's explicit call: all four fields stay fully independent, no cross-block
    // coupling at all, with SplitWarningMessage (below) surfacing any resulting mismatch
    // instead of a dropdown silently narrowing what's selectable.
    public ObservableCollection<PlannerEndTimeOption> SplitFirstEndTimeOptions { get; } = new();
    public ObservableCollection<PlannerEndTimeOption> SplitSecondEndTimeOptions { get; } = new();

    // Live summary shown next to each block's header and the dialog's total — always
    // visible regardless of validity (the architect's explicit ask: don't make "everything
    // looks good" the only way to see the total). Null before both of that piece's fields
    // parse as valid times. SplitOriginalDuration is captured once when the dialog opens
    // (localEnd - localStart) — folded into the dialog's own intro text (option "C") and
    // compared against SplitTotalDuration to drive IsSplitTotalMismatched (option "B": the
    // Total figure's own color plus a short inline note, not a separate message).
    [ObservableProperty] private TimeSpan _splitOriginalDuration;
    [ObservableProperty] private TimeSpan? _splitFirstBlockDuration;
    [ObservableProperty] private TimeSpan? _splitSecondBlockDuration;
    [ObservableProperty] private TimeSpan? _splitTotalDuration;
    [ObservableProperty] private bool _isSplitTotalMismatched;

    // Live, non-blocking — informational only, the same way FormError/SplitError already
    // are elsewhere in this ViewModel. Confirm's own server-side validation stays the real
    // backstop; this exists purely so a real scheduling conflict is visible before the user
    // even tries to confirm, instead of only surfacing after a round trip. Deliberately
    // does NOT check First's End against Second's Start — a prior version did, and the
    // architect explicitly rejected it: Split's whole point is placing each piece anywhere
    // in the day, not necessarily adjacent to each other. Only two things are checked here:
    // do the two pieces (or a piece and any other already-scheduled block) overlap, and does
    // either piece fall outside the governing contract's Work Hours for that day. The
    // total-duration check lives separately now, via IsSplitTotalMismatched above.
    [ObservableProperty] private string? _splitWarningMessage;

    // Resolved once when the dialog opens (the block's own ContractId, and that contract's
    // Work Hours window for FormDate, via GetContractWorkHoursForDateQuery) — reused on every
    // keystroke by RecomputeSplitSummary rather than re-queried live, since neither the
    // contract nor the date can change while this dialog is open. Null WorkHours means no
    // constraint that day (matches AllowsBlockPlacement's own "no policy = no restriction").
    private Guid? _splitContractId;
    private ContractWorkHoursDto? _splitWorkHours;

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

    // ── Selected-contract banner (persists for the contract's full duration, independent
    // of drafting mode). "Selected" rather than "Active": when the viewed week straddles
    // two in-effect contracts (one ending, the next starting mid-week), the switch button
    // below lets the user page between them, so this can hold either one — not only
    // whichever is domain-Active. Everything keyed off it (Cancel, redemption bounds,
    // new-block ContractId attribution) re-targets to whichever is currently selected. ──
    [ObservableProperty] private NenContractRangeViewDto? _selectedContract;
    [ObservableProperty] private bool _isConfirmCancelContractVisible;
    [ObservableProperty] private string _confirmCancelContractMessage = string.Empty;
    [ObservableProperty] private string? _cancelContractErrorMessage;

    // Refreshed alongside PopoverActiveContract in RefreshContractsPopoverAsync — binds the
    // popover's "Complete" button's visibility directly to GetNenContractCompletionReadinessQuery,
    // the same query CompleteNenContractValidator delegates to, so the button and the command's
    // own gate can never drift apart.
    [ObservableProperty] private bool _canCompleteActiveContract;
    [ObservableProperty] private string? _completeContractErrorMessage;

    // Mirrors CanCompleteActiveContract but scoped to SelectedContract (the banner's own
    // contract) rather than the globally-Active one — see RefreshCanCompleteSelectedContractAsync.
    [ObservableProperty] private bool _canCompleteSelectedContract;

    /// <summary>Raised when Complete or Break (Cancel) succeeds, carrying the recap for the
    /// Post-Contract overlay — ShellViewModel subscribes to show it immediately, mirroring
    /// MainPageViewModel's EndSession-to-ShowPostSession precedent.</summary>
    public event Action<NenContractRecapDto>? PostContractReady;

    // Every in-effect contract touching the currently viewed week, default-first (the same
    // order GetNenContractsForDateRangeQuery returns) — SelectedContract is always one of
    // these. Refreshed alongside SelectedContract in RefreshSelectedContractAsync; the
    // selection always resets to the default (index 0) on a week navigation, never carried
    // across a range change.
    private IReadOnlyList<NenContractRangeViewDto> _contractsThisWeek = [];
    private int _selectedContractIndex;

    // ── Nen Contract popover (replaces the old top-right "Make Nen Contract" button).
    // Deliberately independent of SelectedContract/_contractsThisWeek — this is a global
    // "manage your contracts" surface, not scoped to whatever week the Planner grid
    // currently has on screen. The Active row (at most one ever exists) and the Scheduled
    // list both come from one GetInEffectNenContractsQuery call. ──────────────────────────
    [ObservableProperty] private bool _isNenContractPopoverVisible;
    [ObservableProperty] private NenContractSummaryDto? _popoverActiveContract;
    public ObservableCollection<NenContractSummaryDto> ScheduledContracts { get; } = new();
    public bool HasNoContracts => PopoverActiveContract is null && ScheduledContracts.Count == 0;

    [ObservableProperty] private bool _isConfirmDeleteContractVisible;
    [ObservableProperty] private string _confirmDeleteContractMessage = string.Empty;
    [ObservableProperty] private string? _deleteContractErrorMessage;

    private Guid? _contractIdPendingDelete;

    // ── Cover Deficit popover (mirrors the Nen Contract popover's button+Popup shape) ──
    [ObservableProperty] private bool _isCoverDeficitPopoverVisible;
    [ObservableProperty] private string? _coverDeficitErrorMessage;
    public ObservableCollection<SuggestedRedemptionPlacementDto> CoverDeficitOptions { get; } = new();
    public bool HasCoverableDeficit => CoverDeficitOptions.Count > 0;

    // ── Cover Deficit → redemption creation (Open Gap N) ────────────────────────
    // Clicking Cover Deficit opens the ordinary block-creation form, pre-filled at the
    // suggested slot with the preset locked to the deficit's own preset — reusing every
    // existing form/date/time/Split mechanic rather than a bespoke calendar-drag flow.
    // This is now the ONLY path that can produce a redemption-linked block — manual tile
    // creation always declines redemption (see SaveAsync's plain create path). The old
    // pre-create "won't fully cover — create anyway?" warning is gone: it existed only
    // because the previous flow instant-created with no chance to reconsider. Now the user
    // sees an editable form before Save, same as every other block — under-covering the
    // deficit just leaves the remainder outstanding, unremarkable like any other block.
    private bool _isCreatingRedemption;
    private DateOnly? _redemptionContractStartDate;
    private DateOnly? _redemptionContractEndDate;

    public bool HasSelectedContract => SelectedContract is not null;

    /// <summary>The contract banner steps aside while drafting — the drafting banner is the only one then (editing an Upcoming contract would otherwise show both).</summary>
    public bool IsContractBannerVisible => HasSelectedContract && !IsDraftingContract;

    // True only when a second in-effect contract also touches the currently viewed week —
    // drives the banner's switch-button visibility. A simple two-way toggle by design
    // (cycles for the rare 3+-contracts-in-one-week case); no dedicated picker.
    public bool HasOtherContractThisWeek => _contractsThisWeek.Count > 1;

    // Names whichever contract clicking the switch button would jump to next.
    public string SwitchContractButtonText =>
        _contractsThisWeek.Count > 1
            ? $"Switch to {_contractsThisWeek[(_selectedContractIndex + 1) % _contractsThisWeek.Count].Name}"
            : string.Empty;

    // Drives the banner's "Upcoming" tag — reflects real domain Status directly, not
    // switch history, so a future week where nothing has started yet still reads as
    // Upcoming even while it's the default (unswitched) selection.
    public bool IsSelectedContractUpcoming => SelectedContract?.Status == NenContractStatus.Scheduled;

    // One format for every strictness rule — total completed vs. total committed hours,
    // plus outstanding deficit. Uniform across Locked/DailyFlexibility/PeriodTimeBudget
    // (with or without ContractFloor) since TotalFloorHours/TotalCompletedHours are
    // already computed the same way for all of them.
    public string SelectedContractBannerText => SelectedContract is not { } c
        ? string.Empty
        : $"Contract: {c.Name} — Day {c.DayNumber}/{c.TotalDays} — " +
          $"{c.TotalCompletedHours:0.#}h / {c.TotalFloorHours:0.#}h completed" +
          (c.DeficitHours > 0 ? $" — {MinutesToStringConverter.FormatMinutes(c.DeficitHours * 60)} deficit" : string.Empty) +
          (c.TotalBonusHours > 0 ? $" — {MinutesToStringConverter.FormatMinutes(c.TotalBonusHours * 60)} bonus" : string.Empty);

    // 3-second live poll for the day-header floor badges while a selected contract is in
    // view — so today's badge reflects an in-progress session's elapsed hours without
    // waiting for the next week-nav/seal/session-end refresh. No page-visibility gating:
    // this ViewModel already lives for the app's whole lifetime (not recreated on
    // navigation, and there's no such hook anywhere in this codebase), and the cost is
    // comparable to ScheduledEnforcementService's own always-on 5s heartbeat. Started/
    // stopped by OnSelectedContractChanged, not by a page-visible/hidden signal.
    private readonly System.Timers.Timer _floorStatusPollTimer = new(3000) { AutoReset = true };

    partial void OnSelectedContractChanged(NenContractRangeViewDto? value)
    {
        OnPropertyChanged(nameof(HasSelectedContract));
        OnPropertyChanged(nameof(IsContractBannerVisible));
        OnPropertyChanged(nameof(SelectedContractBannerText));
        OnPropertyChanged(nameof(HasNoContracts));
        OnPropertyChanged(nameof(IsSelectedContractUpcoming));
        OnPropertyChanged(nameof(HasOtherContractThisWeek));
        OnPropertyChanged(nameof(SwitchContractButtonText));

        if (value is not null) _floorStatusPollTimer.Start();
        else _floorStatusPollTimer.Stop();
    }

    // Scoped to the currently viewed week — badges and the banner only reflect contracts
    // whose date range overlaps the week actually on screen, not just "some contract is in
    // effect somewhere." Must be re-run on every week navigation, not only on init/seal/
    // session-end. Always resets the banner selection to the default (index 0) — a
    // switched-to contract is a momentary "peek," never carried forward to a different week.
    // The calendar's day badges are independent of that selection entirely (see
    // RebuildMergedDayFloorByDate) — every contract touching the week always shows its own
    // days, regardless of which one the banner currently names.
    private async Task RefreshSelectedContractAsync()
    {
        _contractsThisWeek = await FetchContractsThisWeekAsync();
        _selectedContractIndex = 0;
        await ApplySelectedIndexAsync();
    }

    // Re-fetches live data for the currently viewed week (elapsed hours, deficit, banner
    // numbers, merged badges) without disturbing a manually switched banner selection —
    // used by every incidental refresh that isn't an actual week navigation (the 3-second
    // live poll, saving/dragging/splitting a block, sealing, cancelling). Re-locates the
    // previously selected contract by Id in the freshly fetched list; falls back to the
    // default (index 0) only if that contract is no longer in it — e.g. it was just Broken
    // or Completed by the very refresh that triggered this call.
    private async Task RefreshContractsPreservingSelectionAsync()
    {
        var previouslySelectedId = SelectedContract?.Id;
        _contractsThisWeek = await FetchContractsThisWeekAsync();

        _selectedContractIndex = 0;
        if (previouslySelectedId is { } id)
        {
            for (var i = 0; i < _contractsThisWeek.Count; i++)
            {
                if (_contractsThisWeek[i].Id == id) { _selectedContractIndex = i; break; }
            }
        }

        await ApplySelectedIndexAsync();
    }

    private async Task<IReadOnlyList<NenContractRangeViewDto>> FetchContractsThisWeekAsync()
    {
        var weekStartDate = DateOnly.FromDateTime(WeekStart);
        var weekEndDate = weekStartDate.AddDays(6);
        return await _queryDispatcher.QueryAsync(new GetNenContractsForDateRangeQuery(weekStartDate, weekEndDate));
    }

    private async Task ApplySelectedIndexAsync()
    {
        SelectedContract = _contractsThisWeek.Count > 0 ? _contractsThisWeek[_selectedContractIndex] : null;
        RebuildMergedDayFloorByDate();
        await RefreshCoverDeficitOptionsAsync();
        await RefreshCanCompleteSelectedContractAsync();
    }

    // Drives the banner's own "Complete" button (next to Cover Deficit) — a second surface
    // for the same GetNenContractCompletionReadinessQuery the Nen Contract popover's button
    // already binds to, scoped to whichever contract the banner is currently naming rather
    // than the globally-Active one, since the two can differ (e.g. viewing a Scheduled
    // contract's week while a different one is Active elsewhere).
    private async Task RefreshCanCompleteSelectedContractAsync()
    {
        CanCompleteSelectedContract = SelectedContract is { Status: NenContractStatus.Active } c &&
            (await _queryDispatcher.QueryAsync(new GetNenContractCompletionReadinessQuery(c.Id))).IsReady;
    }

    [RelayCommand]
    private async Task CompleteSelectedContractAsync()
    {
        if (SelectedContract is null) return;
        CompleteContractErrorMessage = null;

        var result = await _commandDispatcher.DispatchAsync(new CompleteNenContractCommand { ContractId = SelectedContract.Id });
        if (!result.Success)
        {
            CompleteContractErrorMessage = result.ErrorMessage;
            return;
        }

        PostContractReady?.Invoke(result.Recap!);
        await RefreshContractsPopoverAsync();
        await RefreshContractsPreservingSelectionAsync();
        await LoadWeekAsync();
    }

    // Every in-effect contract touching the week contributes its own DayStatuses — contract
    // date ranges never overlap, so no two contracts can ever contribute the same date and
    // there is nothing to reconcile. Independent of SelectedContract entirely: switching the
    // banner never adds, removes, or recolors a single badge.
    private void RebuildMergedDayFloorByDate()
    {
        // Drafting owns DayFloorByDate while it's active (RefreshDayFloorPreviewsAsync) —
        // an in-effect contract can never overlap the date range being drafted, so this
        // never runs concurrently with a real draft, but skip anyway rather than relying
        // on that invariant holding forever.
        if (IsDraftingContract) return;

        var merged = new Dictionary<DateOnly, DayFloorPreviewViewModel>();
        foreach (var contract in _contractsThisWeek)
        {
            foreach (var d in contract.DayStatuses)
            {
                merged[d.Date] = new DayFloorPreviewViewModel(
                    d.Date, d.CurrentHours, d.IsRestDay ? null : d.FloorHours, d.IsRestDay, d.Timing,
                    contract.StrictnessRule, contract.ContractFloorHours.HasValue, d.BonusHours,
                    contract.Status, d.IsMet);
            }
        }
        DayFloorByDate = merged;
    }

    // Re-targets the banner text and Cover Deficit options to the other in-effect contract
    // touching this week — a deliberate switch of what the banner names and what Cover
    // Deficit acts on, not a change to the calendar's badges (see RebuildMergedDayFloorByDate,
    // never called here). Cycles through _contractsThisWeek for the rare 3+-in-one-week case
    // rather than a dedicated picker.
    [RelayCommand]
    private async Task SwitchToOtherContractThisWeekAsync()
    {
        if (_contractsThisWeek.Count < 2) return;
        _selectedContractIndex = (_selectedContractIndex + 1) % _contractsThisWeek.Count;
        SelectedContract = _contractsThisWeek[_selectedContractIndex];
        await RefreshCoverDeficitOptionsAsync();
        await RefreshCanCompleteSelectedContractAsync();
    }

    private async Task RefreshCoverDeficitOptionsAsync()
    {
        CoverDeficitOptions.Clear();
        if (SelectedContract is { } contract)
        {
            var suggestions = await _queryDispatcher.QueryAsync(
                new GetSuggestedRedemptionPlacementsQuery(contract.Id, _clockService.LocalNow.ToUniversalTime()));
            foreach (var suggestion in suggestions)
                CoverDeficitOptions.Add(suggestion);
        }
        OnPropertyChanged(nameof(HasCoverableDeficit));
    }

    [RelayCommand]
    private async Task ToggleCoverDeficitPopoverAsync()
    {
        if (IsFormVisible || IsWizardVisible || IsDraftingContract) return;
        if (CoverDeficitOptions.Count == 0) return;
        if (CoverDeficitOptions.Count == 1)
        {
            await OpenRedemptionCreateFormAsync(CoverDeficitOptions[0]);
            return;
        }
        IsCoverDeficitPopoverVisible = !IsCoverDeficitPopoverVisible;
    }

    [RelayCommand]
    private async Task CoverDeficitAsync(SuggestedRedemptionPlacementDto option)
    {
        IsCoverDeficitPopoverVisible = false;
        await OpenRedemptionCreateFormAsync(option);
    }

    // Opens the ordinary block-creation form pre-filled at the suggested slot, preset
    // locked to the deficit's own preset (ShowPresetPicker/SaveAsync enforce this — the
    // form never lets it change here). The user can freely adjust date/time, use Split
    // Block to divide this not-yet-created placement into two, then Save creates it via
    // the exact same CreatePlannedBlockCommand path any other Focus block uses — the only
    // difference is DeclineRedemption stays false, letting attribution attach normally.
    private async Task OpenRedemptionCreateFormAsync(SuggestedRedemptionPlacementDto option)
    {
        await EnsurePresetsLoadedAsync();
        CoverDeficitErrorMessage = null;
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        IsStartLocked = false;
        FormTitle = "Cover Deficit";
        ResetForm();

        _isCreatingRedemption = true;
        if (SelectedContract?.RedemptionPlacementPolicy == RedemptionPlacementPolicy.CurrentDayOnly)
        {
            var today = DateOnly.FromDateTime(_clockService.LocalNow.Date);
            _redemptionContractStartDate = today;
            _redemptionContractEndDate = today;
        }
        else
        {
            _redemptionContractStartDate = SelectedContract?.StartDate;
            _redemptionContractEndDate = SelectedContract?.EndDate;
        }
        OnPropertyChanged(nameof(CanSplitBlock));
        OnPropertyChanged(nameof(CanChangePreset));
        OnPropertyChanged(nameof(ShowTypeToggle));

        IsFocusBlock = true; // redemption is Focus-only
        SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Id == option.PresetId);

        var localStart = option.SuggestedStart.ToLocalTime();
        var localEnd = option.SuggestedEnd.ToLocalTime();
        var blockDate = DateTime.SpecifyKind(localStart.Date, DateTimeKind.Utc);
        FormDate = new DateTimeOffset(blockDate, TimeSpan.Zero);
        SetFormTimes(localStart.TimeOfDay, localEnd.TimeOfDay);

        IsFormVisible = true;
    }

    [RelayCommand]
    private void RequestCancelContract()
    {
        if (PopoverActiveContract is null) return;
        CancelContractErrorMessage = null;
        ConfirmCancelContractMessage = "Are you sure you want to cancel this contract? This cannot be undone.";
        IsConfirmCancelContractVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCancelContractAsync()
    {
        if (PopoverActiveContract is null) { IsConfirmCancelContractVisible = false; return; }

        var result = await _commandDispatcher.DispatchAsync(new BreakNenContractCommand { ContractId = PopoverActiveContract.Id });
        IsConfirmCancelContractVisible = false;

        if (!result.Success)
        {
            CancelContractErrorMessage = result.ErrorMessage;
            return;
        }

        IsNenContractPopoverVisible = false;
        PostContractReady?.Invoke(result.Recap!);
        await RefreshContractsPopoverAsync();
        await RefreshContractsPreservingSelectionAsync();
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
    //
    // The Cover Deficit path (_isCreatingRedemption) is a second, independent way in: the
    // block doesn't exist yet, so none of SplitPlannedBlockValidator's rules apply at all —
    // OpenSplitDialog/ConfirmSplitAsync branch on _isCreatingRedemption and dispatch two
    // CreatePlannedBlockCommands instead of SplitPlannedBlockCommand.
    public bool CanSplitBlock => _isCreatingRedemption || (
        IsEditing && !_editingIsDraft && IsFocusBlock
        && IsEditingBlockContractLocked && IsEditingBlockDailyFlexibility
        && !IsStartLocked && !IsEditingCompleted
        && _sessionState.ActiveSession?.OriginatingBlockId != _editingBlockId);

    // Preset is locked to the deficit's own preset while creating a redemption block —
    // the popover already picked which preset this covers, so re-picking a different one
    // here would silently disconnect the placement from the deficit it exists to cover.
    public bool CanChangePreset => !IsBlockLocked && !_isCreatingRedemption;

    // Hidden once editing an already-placed block (type can't change after the fact — see
    // ARCHITECT_GOVERNANCE.md's sealed-contract enforcement notes) or while creating a
    // redemption block (Focus-only by construction). Still shown for a brand-new ordinary
    // block and for editing an in-progress (unsealed) contract-wizard draft, which remains
    // freely re-typeable until Seal.
    public bool ShowTypeToggle => (!IsEditing || _editingIsDraft) && !_isCreatingRedemption;

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
        OnPropertyChanged(nameof(ShowTypeToggle));
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
    partial void OnIsDraftingContractChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpenContractWizard));
        OnPropertyChanged(nameof(IsContractBannerVisible));
    }

    public PlannerPageViewModel(
        IQueryDispatcher queryDispatcher,
        ICommandDispatcher commandDispatcher,
        IPlannerClockService clockService,
        ISessionStateService sessionState)
    {
        _queryDispatcher = queryDispatcher;
        _commandDispatcher = commandDispatcher;
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
        Dispatcher.UIThread.Post(() => _ = RefreshContractsPreservingSelectionAsync());
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
                _ = RefreshContractsPreservingSelectionAsync();
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
        _timeOptionSpans.Clear();
        for (int h = 0; h < 24; h++)
        {
            for (int m = 0; m < 60; m += 15)
            {
                TimeOptions.Add($"{h:D2}:{m:D2}");
                _timeOptionSpans.Add(new TimeSpan(h, m, 0));
            }
        }
        TimeOptions.Add("24:00");
        _timeOptionSpans.Add(TimeSpan.FromHours(24));
    }

    private async Task InitAsync()
    {
        await LoadPresetsAsync();
        await LoadWeekAsync();
        await RefreshSelectedContractAsync();
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
        await RefreshSelectedContractAsync();
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
        var presets = await _queryDispatcher.QueryAsync(new GetAllBlockPresetsQuery());
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
        RecenterEndTimeScrollIndex();
    }

    partial void OnFormStartTimeChanged(string value)
    {
        IsFormStartTimeInvalid = !TryParseFormTime(value, out var newStart);
        if (!IsFormStartTimeInvalid) RegenerateEndTimeOptions();
        StartTimeScrollIndex = ResolveApproxTime(value) is { } approxStart
            ? PlannerTimeOptionLookup.NearestIndex(approxStart, _timeOptionSpans)
            : null;
        RecenterEndTimeScrollIndex(); // End's own pool just regenerated (or didn't) — either way, re-window it

        if (_isPopulatingFormTimes || IsDraftingContract || IsFormStartTimeInvalid) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedFormDuration);
        _isPopulatingFormTimes = true;
        FormEndTime = $"{(int)newEnd.TotalHours:D2}:{newEnd.Minutes:D2}";
        _isPopulatingFormTimes = false;

        UpdateEndRolloverDisplay(newStart, newEnd);
    }

    partial void OnFormEndTimeChanged(string value)
    {
        IsFormEndTimeInvalid = !TryParseFormTime(value, out var newEnd);
        RecenterEndTimeScrollIndex();

        if (_isPopulatingFormTimes || IsDraftingContract || IsFormEndTimeInvalid) return;
        if (!TryParseFormTime(FormStartTime, out var start)) return;

        _preservedFormDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
        UpdateEndRolloverDisplay(start, newEnd);
    }

    /// <summary>
    /// Best available guess at what time the user is typing toward, for the flat-list
    /// dropdowns' (Start + the four Split fields) live centering and arrow-key navigation —
    /// an exact parse when the text is already a complete valid time, otherwise a
    /// partial-digit hour guess (see PlannerTimeOptionLookup.TryGuessHour), otherwise null
    /// (nothing typed yet to guess from). End works in duration space instead — see
    /// ResolveApproxEndDuration — since its options aren't ordered by wall-clock time.
    /// </summary>
    private static TimeSpan? ResolveApproxTime(string text)
    {
        if (TryParseFormTime(text, out var exact)) return exact;
        if (PlannerTimeOptionLookup.TryGuessHour(text, out var guess)) return guess;
        return null;
    }

    /// <summary>
    /// Shared open/Up/Down logic behind Start's and the four Split fields' custom pickers —
    /// all five operate on the same flat TimeOptions list, differing only in which
    /// Form*Time/*ScrollIndex/Is*DropDownOpen properties they read and write, so the actual
    /// logic lives here once instead of five hand-duplicated copies. Each field still gets
    /// its own thin, named wrapper methods (below) since the View's code-behind needs
    /// concrete, bindable per-field entry points.
    /// </summary>
    private void OpenTimeDropdown(Func<string> getText, Action<bool> setOpen, Action<int?> setScrollIndex)
    {
        setOpen(true);
        setScrollIndex(ResolveApproxTime(getText()) is { } approx ? PlannerTimeOptionLookup.NearestIndex(approx, _timeOptionSpans) : null);
    }

    /// <summary>
    /// Down-arrow: commits the next slot strictly after the current (possibly off-grid or
    /// partial) value — see PlannerTimeOptionLookup.IndexAfter for why this needs no special
    /// case for an off-grid starting value. Commits via <paramref name="setText"/> exactly as
    /// if the user had typed or clicked the result, so each field's own OnXChanged partial
    /// method (shift-math/rollover/redemption-cap side effects) fires identically either way.
    /// </summary>
    private void MoveTimeNext(Func<string> getText, Action<string> setText)
    {
        var current = ResolveApproxTime(getText()) ?? TimeSpan.Zero;
        setText(TimeOptions[PlannerTimeOptionLookup.IndexAfter(current, _timeOptionSpans)]);
    }

    /// <summary>Up-arrow mirror of <see cref="MoveTimeNext"/>.</summary>
    private void MoveTimePrevious(Func<string> getText, Action<string> setText)
    {
        var current = ResolveApproxTime(getText()) ?? TimeSpan.Zero;
        setText(TimeOptions[PlannerTimeOptionLookup.IndexBefore(current, _timeOptionSpans)]);
    }

    /// <summary>Opens Start's custom dropdown and centers it on the current value — called from the View on GotFocus/PointerPressed.</summary>
    public void OpenStartTimeDropdown()
    {
        ShowStartTimeInvalid = false;
        OpenTimeDropdown(() => FormStartTime, v => IsStartTimeDropDownOpen = v, v => StartTimeScrollIndex = v);
    }

    public void CloseStartTimeDropdown() => IsStartTimeDropDownOpen = false;
    public void MoveStartTimeNext() => MoveTimeNext(() => FormStartTime, v => FormStartTime = v);
    public void MoveStartTimePrevious() => MoveTimePrevious(() => FormStartTime, v => FormStartTime = v);

    /// <summary>Clicking an item in Start's popup — commits it and closes the dropdown.</summary>
    public void SelectStartTimeOption(string text)
    {
        FormStartTime = text;
        CloseStartTimeDropdown();
    }

    /// <summary>
    /// End's options aren't ordered by wall-clock time — a long duration can roll End past
    /// midnight, where the resulting time-of-day is numerically smaller than an earlier,
    /// shorter-duration option. Comparing by Duration instead (monotonically increasing by
    /// construction — see RegenerateEndTimeOptions/PlannerEndTimeOptions.GenerateOffsets)
    /// keeps NearestIndex/IndexAfter/IndexBefore correct regardless of any wraparound.
    /// Returns null when Start itself isn't currently valid — there's nothing to resolve a
    /// duration relative to.
    /// </summary>
    private TimeSpan? ResolveApproxEndDuration(string text)
    {
        if (!TryParseFormTime(FormStartTime, out var start)) return null;
        return ResolveApproxTime(text) is { } approxEnd ? PlannerTimeShiftMath.ResolvedDuration(start, approxEnd) : null;
    }

    private void RecenterEndTimeScrollIndex()
    {
        var durations = EndTimeOptions.Select(o => o.Duration).ToList();
        EndTimeScrollIndex = durations.Count > 0 && ResolveApproxEndDuration(FormEndTime) is { } approx
            ? PlannerTimeOptionLookup.NearestIndex(approx, durations)
            : null;
    }

    /// <summary>Opens End's custom dropdown and centers it on the current value — mirrors OpenStartTimeDropdown.</summary>
    public void OpenEndTimeDropdown()
    {
        IsEndTimeDropDownOpen = true;
        ShowEndTimeInvalid = false;
        RecenterEndTimeScrollIndex();
    }

    public void CloseEndTimeDropdown() => IsEndTimeDropDownOpen = false;

    /// <summary>Down-arrow mirror of <see cref="MoveStartTimeNext"/>, in duration space (see <see cref="ResolveApproxEndDuration"/>).</summary>
    public void MoveEndTimeNext()
    {
        var durations = EndTimeOptions.Select(o => o.Duration).ToList();
        if (durations.Count == 0) return;
        var current = ResolveApproxEndDuration(FormEndTime) ?? TimeSpan.Zero;
        var index = PlannerTimeOptionLookup.IndexAfter(current, durations);
        FormEndTime = EndTimeOptions[index].TimeText;
    }

    /// <summary>Up-arrow mirror of <see cref="MoveEndTimeNext"/>.</summary>
    public void MoveEndTimePrevious()
    {
        var durations = EndTimeOptions.Select(o => o.Duration).ToList();
        if (durations.Count == 0) return;
        var current = ResolveApproxEndDuration(FormEndTime) ?? TimeSpan.Zero;
        var index = PlannerTimeOptionLookup.IndexBefore(current, durations);
        FormEndTime = EndTimeOptions[index].TimeText;
    }

    /// <summary>Clicking an item in End's popup — commits it and closes the dropdown.</summary>
    public void SelectEndTimeOption(string text)
    {
        FormEndTime = text;
        CloseEndTimeDropdown();
    }

    /// <summary>
    /// Generalizes ResolveApproxEndDuration for any Start/End pair backed by a duration-
    /// labeled option list — used by Split's First/Second End fields (relative to that
    /// piece's own Start, not FormStartTime). End's own methods above are left as their
    /// original, separately-verified bespoke implementation rather than retrofitted onto
    /// this, to avoid touching already-confirmed-working code for a purely cosmetic
    /// de-duplication.
    /// </summary>
    private TimeSpan? ResolveApproxDurationFrom(string startText, string endText)
    {
        if (!TryParseFormTime(startText, out var start)) return null;
        return ResolveApproxTime(endText) is { } approxEnd ? PlannerTimeShiftMath.ResolvedDuration(start, approxEnd) : null;
    }

    private void OpenDurationDropdown(Func<string> getStartText, Func<string> getEndText, ObservableCollection<PlannerEndTimeOption> options, Action<bool> setOpen, Action<int?> setScrollIndex)
    {
        setOpen(true);
        var durations = options.Select(o => o.Duration).ToList();
        setScrollIndex(durations.Count > 0 && ResolveApproxDurationFrom(getStartText(), getEndText()) is { } approx
            ? PlannerTimeOptionLookup.NearestIndex(approx, durations)
            : null);
    }

    private void MoveDurationNext(Func<string> getStartText, Func<string> getEndText, ObservableCollection<PlannerEndTimeOption> options, Action<string> setEndText)
    {
        var durations = options.Select(o => o.Duration).ToList();
        if (durations.Count == 0) return;
        var current = ResolveApproxDurationFrom(getStartText(), getEndText()) ?? TimeSpan.Zero;
        setEndText(options[PlannerTimeOptionLookup.IndexAfter(current, durations)].TimeText);
    }

    private void MoveDurationPrevious(Func<string> getStartText, Func<string> getEndText, ObservableCollection<PlannerEndTimeOption> options, Action<string> setEndText)
    {
        var durations = options.Select(o => o.Duration).ToList();
        if (durations.Count == 0) return;
        var current = ResolveApproxDurationFrom(getStartText(), getEndText()) ?? TimeSpan.Zero;
        setEndText(options[PlannerTimeOptionLookup.IndexBefore(current, durations)].TimeText);
    }

    public void OpenSplitFirstStartTimeDropdown() =>
        OpenTimeDropdown(() => SplitFirstStartTime, v => IsSplitFirstStartTimeDropDownOpen = v, v => SplitFirstStartTimeScrollIndex = v);
    public void CloseSplitFirstStartTimeDropdown() => IsSplitFirstStartTimeDropDownOpen = false;
    public void MoveSplitFirstStartTimeNext() => MoveTimeNext(() => SplitFirstStartTime, v => SplitFirstStartTime = v);
    public void MoveSplitFirstStartTimePrevious() => MoveTimePrevious(() => SplitFirstStartTime, v => SplitFirstStartTime = v);
    public void SelectSplitFirstStartTimeOption(string text) { SplitFirstStartTime = text; CloseSplitFirstStartTimeDropdown(); }

    public void OpenSplitFirstEndTimeDropdown() =>
        OpenDurationDropdown(() => SplitFirstStartTime, () => SplitFirstEndTime, SplitFirstEndTimeOptions, v => IsSplitFirstEndTimeDropDownOpen = v, v => SplitFirstEndTimeScrollIndex = v);
    public void CloseSplitFirstEndTimeDropdown() => IsSplitFirstEndTimeDropDownOpen = false;
    public void MoveSplitFirstEndTimeNext() => MoveDurationNext(() => SplitFirstStartTime, () => SplitFirstEndTime, SplitFirstEndTimeOptions, v => SplitFirstEndTime = v);
    public void MoveSplitFirstEndTimePrevious() => MoveDurationPrevious(() => SplitFirstStartTime, () => SplitFirstEndTime, SplitFirstEndTimeOptions, v => SplitFirstEndTime = v);
    public void SelectSplitFirstEndTimeOption(string text) { SplitFirstEndTime = text; CloseSplitFirstEndTimeDropdown(); }

    public void OpenSplitSecondStartTimeDropdown() =>
        OpenTimeDropdown(() => SplitSecondStartTime, v => IsSplitSecondStartTimeDropDownOpen = v, v => SplitSecondStartTimeScrollIndex = v);
    public void CloseSplitSecondStartTimeDropdown() => IsSplitSecondStartTimeDropDownOpen = false;
    public void MoveSplitSecondStartTimeNext() => MoveTimeNext(() => SplitSecondStartTime, v => SplitSecondStartTime = v);
    public void MoveSplitSecondStartTimePrevious() => MoveTimePrevious(() => SplitSecondStartTime, v => SplitSecondStartTime = v);
    public void SelectSplitSecondStartTimeOption(string text) { SplitSecondStartTime = text; CloseSplitSecondStartTimeDropdown(); }

    public void OpenSplitSecondEndTimeDropdown() =>
        OpenDurationDropdown(() => SplitSecondStartTime, () => SplitSecondEndTime, SplitSecondEndTimeOptions, v => IsSplitSecondEndTimeDropDownOpen = v, v => SplitSecondEndTimeScrollIndex = v);
    public void CloseSplitSecondEndTimeDropdown() => IsSplitSecondEndTimeDropDownOpen = false;
    public void MoveSplitSecondEndTimeNext() => MoveDurationNext(() => SplitSecondStartTime, () => SplitSecondEndTime, SplitSecondEndTimeOptions, v => SplitSecondEndTime = v);
    public void MoveSplitSecondEndTimePrevious() => MoveDurationPrevious(() => SplitSecondStartTime, () => SplitSecondEndTime, SplitSecondEndTimeOptions, v => SplitSecondEndTime = v);
    public void SelectSplitSecondEndTimeOption(string text) { SplitSecondEndTime = text; CloseSplitSecondEndTimeDropdown(); }

    /// <summary>Wired from the View's code-behind on the Start field's LostFocus — reveals
    /// the red border if what's actually sitting in the field right now is invalid. The
    /// value itself is left exactly as typed (see ShowStartTimeInvalid's own doc comment).</summary>
    public void OnFormStartTimeLostFocus() => ShowStartTimeInvalid = IsFormStartTimeInvalid;

    /// <summary>Wired from the View's code-behind on the End field's LostFocus — mirrors OnFormStartTimeLostFocus.</summary>
    public void OnFormEndTimeLostFocus() => ShowEndTimeInvalid = IsFormEndTimeInvalid;

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

            EndTimeOptions.Add(new PlannerEndTimeOption($"{(int)end.TotalHours:D2}:{end.Minutes:D2}", offset));
        }
    }

    /// <summary>
    /// Split's own version of RegenerateEndTimeOptions, for First's End / Second's Start
    /// dropdowns. Unlike the main form, the same-day cutoff always applies here (not gated
    /// on IsDraftingContract) — the Split dialog's own stated rule is that both resulting
    /// pieces stay on the same day as the original block, unconditionally.
    /// </summary>
    private void RegenerateSplitEndTimeOptions(string startText, ObservableCollection<PlannerEndTimeOption> target)
    {
        target.Clear();
        if (!TryParseFormTime(startText, out var start)) return;

        foreach (var offset in PlannerEndTimeOptions.GenerateOffsets(IsFocusBlock))
        {
            var end = PlannerTimeShiftMath.ShiftEnd(start, offset);
            if (PlannerTimeShiftMath.EndRollsToNextDay(start, end))
                break;

            target.Add(new PlannerEndTimeOption($"{(int)end.TotalHours:D2}:{end.Minutes:D2}", offset));
        }
    }

    /// <summary>
    /// Recomputes the always-visible First/Second/Total duration summary,
    /// IsSplitTotalMismatched, and the non-blocking SplitWarningMessage — called
    /// unconditionally (before any _isPopulatingSplitTimes early-return) from all four
    /// Split OnXChanged handlers and once from OpenSplitDialogAsync, so it reflects
    /// whatever's currently in the four fields regardless of whether the last change was
    /// typed or a programmatic shift-preserve.
    /// </summary>
    private void RecomputeSplitSummary()
    {
        var firstStartOk = TryParseFormTime(SplitFirstStartTime, out var firstStart);
        var firstEndOk = TryParseFormTime(SplitFirstEndTime, out var firstEnd);
        var haveFirst = firstStartOk && firstEndOk;

        var secondStartOk = TryParseFormTime(SplitSecondStartTime, out var secondStart);
        var secondEndOk = TryParseFormTime(SplitSecondEndTime, out var secondEnd);
        var haveSecond = secondStartOk && secondEndOk;

        SplitFirstBlockDuration = haveFirst ? PlannerTimeShiftMath.ResolvedDuration(firstStart, firstEnd) : null;
        SplitSecondBlockDuration = haveSecond ? PlannerTimeShiftMath.ResolvedDuration(secondStart, secondEnd) : null;
        SplitTotalDuration = haveFirst && haveSecond ? SplitFirstBlockDuration + SplitSecondBlockDuration : null;
        IsSplitTotalMismatched = SplitTotalDuration is { } total && total != SplitOriginalDuration;

        if (!haveFirst || !haveSecond)
        {
            SplitWarningMessage = null;
            return;
        }

        SplitWarningMessage = FindSplitOverlap(firstStart, firstEnd, secondStart, secondEnd)
            ?? FindSplitWorkHoursViolation(firstStart, firstEnd, secondStart, secondEnd);
    }

    /// <summary>
    /// Checks the two resulting pieces against each other, then each against every other
    /// already-scheduled block that day (excluding the original block being split itself —
    /// its own footprint would otherwise trivially "conflict" with both pieces). Both
    /// pieces are assumed to land on FormDate — the dialog has no per-piece date picker, so
    /// this matches the only placement it can actually produce (mirrors
    /// SplitPlannedBlockValidator's own "no recursive crossing" assumption).
    /// </summary>
    private string? FindSplitOverlap(TimeSpan firstStart, TimeSpan firstEnd, TimeSpan secondStart, TimeSpan secondEnd)
    {
        if (firstStart < secondEnd && secondStart < firstEnd)
            return "First Block and Second Block overlap each other.";

        var dayDate = FormDate.Date;
        DateTime ToUtc(TimeSpan t) => DateTime.SpecifyKind(dayDate.Add(t), DateTimeKind.Local).ToUniversalTime();

        string? CheckAgainstOthers(string label, TimeSpan start, TimeSpan end)
        {
            var startUtc = ToUtc(start);
            var endUtc = ToUtc(end);
            foreach (var other in _blocksInWeek.Values)
            {
                if (other.Id == _editingBlockId) continue;
                if (other.Start < endUtc && startUtc < other.End)
                {
                    var otherStart = other.Start.ToLocalTime();
                    var otherEnd = other.End.ToLocalTime();
                    return $"{label} overlaps an existing block ({DateTimeRangeToLabelConverter.FormatRange24Hour(otherStart, otherEnd)}).";
                }
            }
            return null;
        }

        return CheckAgainstOthers("First Block", firstStart, firstEnd) ?? CheckAgainstOthers("Second Block", secondStart, secondEnd);
    }

    /// <summary>
    /// Uses the SAME ContractDay.Contains rule AllowsBlockPlacement checks internally, against
    /// the window GetContractWorkHoursForDateQuery already resolved once in
    /// OpenSplitDialogAsync — not re-deriving the rule, just applying it locally to avoid a
    /// query per keystroke.
    /// </summary>
    private string? FindSplitWorkHoursViolation(TimeSpan firstStart, TimeSpan firstEnd, TimeSpan secondStart, TimeSpan secondEnd)
    {
        if (_splitWorkHours is not { } wh) return null;

        var window = new ContractDay(DateOnly.FromDateTime(wh.Start), wh.Start, wh.End, floor: null);
        var dayDate = FormDate.Date;
        var windowText = WorkHoursRangeToLabelConverter.FormatKnownWindow12Hour(TimeOnly.FromDateTime(wh.Start), TimeOnly.FromDateTime(wh.End));

        if (!window.Contains(dayDate.Add(firstStart), dayDate.Add(firstEnd)))
            return $"First Block falls outside the contract's hours ({windowText}).";
        if (!window.Contains(dayDate.Add(secondStart), dayDate.Add(secondEnd)))
            return $"Second Block falls outside the contract's hours ({windowText}).";
        return null;
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
            var blocks = await _queryDispatcher.QueryAsync(query);

            foreach (var b in blocks)
            {
                // Editing a Scheduled contract: its saved blocks are represented by their draft
                // copies (seeded in BeginDrafting), so they're left out here — otherwise each shows
                // twice and the saved one, protected by its sealed contract, sits on top of the
                // editable copy. Nothing changes in the database until Seal; cancel restores them.
                if (IsDraftingContract && b.ContractId is { } contractId && contractId == _draftRules?.EditingContractId)
                    continue;

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
            ? await _queryDispatcher.QueryAsync(new GetSessionSnapshotForBlockQuery { PlannedBlockId = dto.Id })
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

        var result = await _commandDispatcher.DispatchAsync(new DeletePlannedBlockCommand { BlockId = block.Id });
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
        _isCreatingRedemption = false;
        _redemptionContractStartDate = null;
        _redemptionContractEndDate = null;
        OnPropertyChanged(nameof(CanSplitBlock));
        OnPropertyChanged(nameof(CanChangePreset));
        OnPropertyChanged(nameof(ShowTypeToggle));
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
        DragError = null;
        if (SelectedPreset is null) { FormError = "Please select a block preset."; return; }
        if (!TryParseFormTime(FormStartTime, out var startTime) || !TryParseFormTime(FormEndTime, out var endTime))
        {
            FormError = "Invalid time format. Use HH:mm.";
            return;
        }
        var (startUtc, endUtc) = ResolveFormRangeUtc(startTime, endTime);

        var blockPresetType = IsFocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;

        if (_isCreatingRedemption)
        {
            var formDateOnly = DateOnly.FromDateTime(FormDate.Date);
            if (_redemptionContractStartDate is { } rangeStart && _redemptionContractEndDate is { } rangeEnd
                && (formDateOnly < rangeStart || formDateOnly > rangeEnd))
            {
                FormError = SelectedContract?.RedemptionPlacementPolicy == RedemptionPlacementPolicy.CurrentDayOnly
                    ? $"This contract only allows redemption on the current day ({DateRangeToLabelConverter.FormatDateShort(rangeStart)})."
                    : $"This must be placed within the contract's date range ({DateRangeToLabelConverter.FormatRangeShort(rangeStart, rangeEnd)}).";
                return;
            }
        }

        if (IsEditing && _editingBlockId.HasValue)
        {
            var typeChanged = _blocksInWeek.TryGetValue(_editingBlockId.Value, out var originalDto)
                && originalDto.BlockPresetType != blockPresetType;

            if (typeChanged)
            {
                var deleteResult = await _commandDispatcher.DispatchAsync(
                    new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
                if (!deleteResult.Success) { FormError = deleteResult.ErrorMessage; return; }

                var createCmd = new CreatePlannedBlockCommand
                {
                    PresetId = SelectedPreset.Id,
                    BlockPresetType = blockPresetType,
                    Start = startUtc,
                    End = endUtc,
                    ContractId = originalDto!.ContractId,
                    // A type change is still an edit, not a new manual placement — only an
                    // already-redemption-linked block (non-null ContractId) gets its
                    // attribution recomputed against the same contract; an ordinary block
                    // never gets newly linked just because its type changed.
                    DeclineRedemption = originalDto.ContractId is null
                };
                var createResult = await _commandDispatcher.DispatchAsync(createCmd);
                if (!createResult.Success) { FormError = createResult.ErrorMessage; return; }
                if (createResult.RedemptionCapped) DragError = RedemptionCapMessage;
            }
            else
            {
                var result = await _commandDispatcher.DispatchAsync(new UpdatePlannedBlockCommand
                {
                    BlockId = _editingBlockId.Value,
                    PresetId = SelectedPreset.Id,
                    Start = startUtc,
                    End = endUtc
                });
                if (!result.Success) { FormError = result.ErrorMessage; return; }
                if (result.RedemptionCapped) DragError = RedemptionCapMessage;
            }
        }
        else
        {
            // Manual creation never links to a deficit, even if the preset+time happens to
            // match one — the only way to create a redemption-linked block is the Cover
            // Deficit flow (_isCreatingRedemption), which leaves DeclineRedemption false so
            // attribution attaches normally. Every other "new block" path always declines —
            // always an ordinary block (or a Bonus block, read-time-labelled, if it falls
            // inside a contract's date range).
            var result = await _commandDispatcher.DispatchAsync(new CreatePlannedBlockCommand
            {
                PresetId = SelectedPreset.Id,
                BlockPresetType = blockPresetType,
                Start = startUtc,
                End = endUtc,
                DeclineRedemption = !_isCreatingRedemption
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
            if (_isCreatingRedemption && result.RedemptionCapped) DragError = RedemptionCapMessage;
        }
        await FinishBlockSavedAsync();
    }

    // Shared by SaveAsync's own non-gated paths and both create-time redemption confirm
    // outcomes below — the refresh/close sequence is identical regardless of which path
    // actually created or updated the block.
    private async Task FinishBlockSavedAsync()
    {
        await LoadWeekAsync();
        await RefreshContractsPreservingSelectionAsync();
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

        var result = await _commandDispatcher.DispatchAsync(new DeletePlannedBlockCommand { BlockId = _editingBlockId.Value });
        if (result.Success) { await LoadWeekAsync(); CloseForm(); }
        else { FormError = result.ErrorMessage; }
    }

    // ── Split Block ────────────────────────────────────────────────────────────

    // Pre-fills a 50/50 split of the block currently open in the edit form — the two
    // resulting ranges are placed back-to-back; repositioning either half afterward is a
    // separate, ordinary drag/move operation, not part of this command.
    [RelayCommand]
    private async Task OpenSplitDialogAsync()
    {
        if (!CanSplitBlock) return;

        DateTime localStart, localEnd;
        Guid? contractId;
        if (_isCreatingRedemption)
        {
            // Nothing persisted yet — bisect whatever the form currently holds, using the
            // same day-rollover resolution Save itself uses so a redemption placement that
            // crosses midnight splits correctly too. No _blocksInWeek entry exists yet
            // either, so the contract comes from SelectedContract instead (the same source
            // _redemptionContractStartDate/EndDate were populated from when redemption mode
            // began — see OpenRedemptionCreateFormAsync).
            if (!TryParseFormTime(FormStartTime, out var formStart) || !TryParseFormTime(FormEndTime, out var formEnd)) return;
            var (startUtc, endUtc) = ResolveFormRangeUtc(formStart, formEnd);
            localStart = startUtc.ToLocalTime();
            localEnd = endUtc.ToLocalTime();
            contractId = SelectedContract?.Id;
        }
        else
        {
            if (!_editingBlockId.HasValue) return;
            if (!_blocksInWeek.TryGetValue(_editingBlockId.Value, out var dto)) return;
            localStart = dto.Start.ToLocalTime();
            localEnd = dto.End.ToLocalTime();
            contractId = dto.ContractId;
        }

        SplitError = null;
        var midpoint = localStart + TimeSpan.FromTicks((localEnd - localStart).Ticks / 2);

        _isPopulatingSplitTimes = true;
        SplitFirstStartTime = $"{localStart.Hour:D2}:{localStart.Minute:D2}";
        SplitFirstEndTime = $"{midpoint.Hour:D2}:{midpoint.Minute:D2}";
        SplitSecondStartTime = $"{midpoint.Hour:D2}:{midpoint.Minute:D2}";
        SplitSecondEndTime = $"{localEnd.Hour:D2}:{localEnd.Minute:D2}";
        _isPopulatingSplitTimes = false;

        _preservedSplitFirstDuration = midpoint - localStart;
        _preservedSplitSecondDuration = localEnd - midpoint;
        SplitOriginalDuration = localEnd - localStart;

        // Resolved once here rather than per-keystroke: neither the contract nor the split's
        // date can change while this dialog is open, so RecomputeSplitSummary (called on
        // every field edit) just reuses this cached window instead of re-querying live.
        _splitContractId = contractId;
        _splitWorkHours = contractId is { } id
            ? await _queryDispatcher.QueryAsync(new GetContractWorkHoursForDateQuery(id, DateOnly.FromDateTime(localStart)))
            : null;

        RegenerateSplitEndTimeOptions(SplitFirstStartTime, SplitFirstEndTimeOptions);
        RegenerateSplitEndTimeOptions(SplitSecondStartTime, SplitSecondEndTimeOptions);
        RecomputeSplitSummary();

        IsSplitDialogVisible = true;
    }

    /// <summary>Shared re-centering logic behind the four Split fields' OnXChanged handlers — mirrors what Start's own OnFormStartTimeChanged computes inline.</summary>
    private void RecenterTimeScrollIndex(string text, Action<int?> setScrollIndex) =>
        setScrollIndex(ResolveApproxTime(text) is { } approx ? PlannerTimeOptionLookup.NearestIndex(approx, _timeOptionSpans) : null);

    partial void OnSplitFirstStartTimeChanged(string value)
    {
        RecenterTimeScrollIndex(value, v => SplitFirstStartTimeScrollIndex = v);
        RegenerateSplitEndTimeOptions(value, SplitFirstEndTimeOptions);
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes || !TryParseFormTime(value, out var newStart)) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedSplitFirstDuration);
        _isPopulatingSplitTimes = true;
        SplitFirstEndTime = $"{(int)newEnd.TotalHours:D2}:{newEnd.Minutes:D2}";
        _isPopulatingSplitTimes = false;
    }

    partial void OnSplitFirstEndTimeChanged(string value)
    {
        RecenterTimeScrollIndex(value, v => SplitFirstEndTimeScrollIndex = v);
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes) return;
        if (!TryParseFormTime(SplitFirstStartTime, out var start) || !TryParseFormTime(value, out var newEnd)) return;

        _preservedSplitFirstDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
    }

    partial void OnSplitSecondStartTimeChanged(string value)
    {
        RecenterTimeScrollIndex(value, v => SplitSecondStartTimeScrollIndex = v);
        RegenerateSplitEndTimeOptions(value, SplitSecondEndTimeOptions);
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes || !TryParseFormTime(value, out var newStart)) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedSplitSecondDuration);
        _isPopulatingSplitTimes = true;
        SplitSecondEndTime = $"{(int)newEnd.TotalHours:D2}:{newEnd.Minutes:D2}";
        _isPopulatingSplitTimes = false;
    }

    partial void OnSplitSecondEndTimeChanged(string value)
    {
        RecenterTimeScrollIndex(value, v => SplitSecondEndTimeScrollIndex = v);
        RecomputeSplitSummary();
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
        if (!_isCreatingRedemption && !_editingBlockId.HasValue) return;
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

        if (_isCreatingRedemption)
        {
            // Nothing exists yet, so there's no OriginalBlockId to split — two ordinary
            // CreatePlannedBlockCommands instead, each independently redemption-eligible
            // exactly like the single-piece Save path. Not atomic across the two calls
            // (no multi-command transaction exists anywhere in this codebase — see
            // DailyFlexibility's own "no multi-block atomic edit" note); a first-succeeds/
            // second-fails outcome leaves the first half created, surfaced explicitly below
            // rather than silently.
            if (SelectedPreset is null) { SplitError = "Please select a block preset."; return; }

            var firstResult = await _commandDispatcher.DispatchAsync(new CreatePlannedBlockCommand
            {
                PresetId = SelectedPreset.Id,
                BlockPresetType = BlockPresetType.Focus,
                Start = firstStartUtc,
                End = firstEndUtc
            });
            if (!firstResult.Success) { SplitError = firstResult.ErrorMessage; return; }

            var secondResult = await _commandDispatcher.DispatchAsync(new CreatePlannedBlockCommand
            {
                PresetId = SelectedPreset.Id,
                BlockPresetType = BlockPresetType.Focus,
                Start = secondStartUtc,
                End = secondEndUtc
            });
            if (!secondResult.Success)
            {
                SplitError = $"First half created, but the second failed: {secondResult.ErrorMessage}";
                return;
            }

            IsSplitDialogVisible = false;
            await FinishBlockSavedAsync();
            return;
        }

        var result = await _commandDispatcher.DispatchAsync(new SplitPlannedBlockCommand
        {
            OriginalBlockId = _editingBlockId!.Value,
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
        await RefreshContractsPreservingSelectionAsync(); // badge refresh — same mechanism drag-end and form-save now also use
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

        var updateResult = await _commandDispatcher.DispatchAsync(new UpdatePlannedBlockCommand
        {
            BlockId   = result.BlockId,
            PresetId = dto.PresetId,
            Start     = result.NewStartUtc,
            End       = result.NewEndUtc
        });

        if (!updateResult.Success)
            DragError = updateResult.ErrorMessage;
        else if (updateResult.RedemptionCapped)
            DragError = RedemptionCapMessage;

        // Reload either way: on success to reflect the committed change, on failure so the
        // tile snaps back to its actual (unchanged) position — the drag gesture already
        // moved it visually, and without this it would stay stuck at the rejected drop
        // point until some unrelated reload happened to fire.
        await LoadWeekAsync();
        // Badge/banner refresh — previously only caught up on the next 3s poll tick,
        // which read as a laggy update right after the drag that actually caused it.
        await RefreshContractsPreservingSelectionAsync();
    }

    // ── Nen Contract wizard + drafting mode ───────────────────────────────────

    [RelayCommand]
    private void OpenContractWizard()
    {
        if (!CanOpenContractWizard) return;
        _editingContractPayload = null;
        IsNenContractPopoverVisible = false;
        WizardViewModel = new ContractWizardViewModel(
            queryDispatcher: _queryDispatcher,
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

        var payload = await _queryDispatcher.QueryAsync(new GetNenContractForEditQuery(contractId));
        if (payload is null) return;

        _editingContractPayload = payload;
        IsNenContractPopoverVisible = false;
        WizardViewModel = new ContractWizardViewModel(
            queryDispatcher: _queryDispatcher,
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
                return $"Blocks must be placed within the contract's date range ({DateRangeToLabelConverter.FormatRangeShort(_draftRules.StartDate, _draftRules.EndDate)}).";

            if (isFocus && _draftRules.WorkHoursStart is { } workHoursStart && _draftRules.WorkHoursEnd is { } workHoursEnd)
            {
                var workHours = new WorkHours(workHoursStart, workHoursEnd);
                if (!workHours.ContainsRange(localStart, localEnd))
                    return $"This block falls outside the contract's Work Hours ({WorkHoursRangeToLabelConverter.FormatKnownWindow12Hour(workHoursStart, workHoursEnd)}).";
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

        var hoursByDay = await _queryDispatcher.QueryAsync(
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
            var isMet = isRestDay || requiredFloor is null || scheduled >= requiredFloor.Value;

            byDate[date] = new DayFloorPreviewViewModel(date, scheduled, requiredFloor, isRestDay, IsMet: isMet);
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
                var editResult = await _commandDispatcher.DispatchAsync(new EditNenContractCommand
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
                    ContractPresetId = _draftRules.ContractPresetId,
                    RedemptionPlacementPolicy = _draftRules.RedemptionPlacementPolicy,
                    KeepBlockingDuringBreaks = _draftRules.KeepBlockingDuringBreaks
                });
                success = editResult.Success;
                errorMessage = editResult.ErrorMessage;
            }
            else
            {
                var sealResult = await _commandDispatcher.DispatchAsync(new SealNenContractCommand
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
                    ContractPresetId = _draftRules.ContractPresetId,
                    RedemptionPlacementPolicy = _draftRules.RedemptionPlacementPolicy,
                    KeepBlockingDuringBreaks = _draftRules.KeepBlockingDuringBreaks
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
            await RefreshContractsPreservingSelectionAsync();
            await RefreshContractsPopoverAsync();
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
        await RefreshContractsPreservingSelectionAsync();
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

        await RefreshContractsPopoverAsync();
        IsNenContractPopoverVisible = true;
    }

    [RelayCommand]
    private void CloseNenContractPopover() => IsNenContractPopoverVisible = false;

    // Jumps the week view to whichever week contains the given contract's first day —
    // bound to the info area of both the Active row and each Upcoming row in the Nen
    // Contract popover, so clicking a contract takes you straight to where it starts.
    [RelayCommand]
    private async Task GoToContractStartAsync(DateOnly startDate)
    {
        IsNenContractPopoverVisible = false;
        await NavigateWeekAsync(StartOfWeek(startDate.ToDateTime(TimeOnly.MinValue)));
    }

    private async Task RefreshContractsPopoverAsync()
    {
        var contracts = await _queryDispatcher.QueryAsync(new GetInEffectNenContractsQuery());
        PopoverActiveContract = contracts.FirstOrDefault(c => c.Status == NenContractStatus.Active);

        ScheduledContracts.Clear();
        foreach (var contract in contracts.Where(c => c.Status == NenContractStatus.Scheduled))
            ScheduledContracts.Add(contract);

        OnPropertyChanged(nameof(HasNoContracts));

        CanCompleteActiveContract = PopoverActiveContract is not null &&
            (await _queryDispatcher.QueryAsync(new GetNenContractCompletionReadinessQuery(PopoverActiveContract.Id))).IsReady;
    }

    [RelayCommand]
    private async Task CompleteContractAsync()
    {
        if (PopoverActiveContract is null) return;
        CompleteContractErrorMessage = null;

        var result = await _commandDispatcher.DispatchAsync(new CompleteNenContractCommand { ContractId = PopoverActiveContract.Id });
        if (!result.Success)
        {
            CompleteContractErrorMessage = result.ErrorMessage;
            return;
        }

        IsNenContractPopoverVisible = false;
        PostContractReady?.Invoke(result.Recap!);
        await RefreshContractsPopoverAsync();
        await RefreshContractsPreservingSelectionAsync();
        await LoadWeekAsync();
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

        var result = await _commandDispatcher.DispatchAsync(new DeleteNenContractCommand { ContractId = contractId });
        IsConfirmDeleteContractVisible = false;
        _contractIdPendingDelete = null;

        if (!result.Success)
        {
            DeleteContractErrorMessage = result.ErrorMessage;
            return;
        }

        await RefreshContractsPopoverAsync();
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
