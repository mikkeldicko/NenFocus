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
using NenFocus.Application.Queries.CheckNenContractCancellable;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetNenContractsForDateRange;
using NenFocus.Application.Queries.GetAllBlockPresets;
using NenFocus.Application.Queries.GetAllContractPresets;
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
using NenFocus.Application.UseCases.Planner.CoverDeficit;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Converters;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Planner.ContractWizard;

namespace NenFocus.ViewModels.Planner;

public partial class PlannerPageViewModel : ViewModelBase, IDisposable
{
    public const double HourRowHeight = 56.0;
    public const int FirstHour = 0;
    public const int LastHour = 24;
    public const int TotalHours = LastHour - FirstHour;
    public double GridContentHeight => HourRowHeight * TotalHours;

    // Shown when Cover Deficit's save comes back WasCapped — the debt shrank between opening the
    // form and saving (Gap 106, decision 19).
    private const string RedemptionCapMessage =
        "A redemption block can't be longer than the deficit it covers — shortened to match.";

    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IDisplayClock _displayClock;
    private readonly ISystemClock _clock;

    /// <summary>What the user's clock shows now — through the user's time zone (Gap 88).</summary>
    private DateTime LocalNow => _clock.TimeZone.ToCalendarTime(_clock.Now);
    private readonly IActiveSessionState _sessionState;
    private readonly IRecapRelay _recaps;

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
            int day = (int)LocalNow.DayOfWeek;
            int mondayBasedIndex = (day == 0) ? 6 : day - 1;
            return mondayBasedIndex + 1;
        }
    }

    public ObservableCollection<WeekDayHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<HourLabelViewModel> HourLabels { get; } = new();
    public ObservableCollection<PlannedBlockTileViewModel> Tiles { get; } = new();
    public ObservableCollection<BlockPresetDto> AvailablePresets { get; } = new();

    // The End field's choices, as times of day — regenerated from Start (and the block type, for
    // the 12 h cap) whenever either changes; see RegenerateEndTimeOptions. The six time fields are
    // TimeOfDayBox controls: typing, the dropdown and the text are theirs (Gap 89 Slice B).
    [ObservableProperty] private IReadOnlyList<TimeSpan> _endTimeOptions = [];

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

    // Null while the field holds text that isn't a time (the field shows it red).
    [ObservableProperty] private TimeSpan? _formStartTime = TimeSpan.FromHours(9);
    [ObservableProperty] private TimeSpan? _formEndTime = TimeSpan.FromHours(10);

    // Google-Calendar-style: a typed/picked End at or before Start resolves to the next
    // day rather than being rejected outright. Non-draft form only — drafting a contract
    // keeps today's same-day-only behavior unchanged (see ValidateDraftPlacement's own
    // non-day-segmented Work Hours check, deliberately not extended to cross-midnight
    // either, per Gap K Part 1's own scope note).
    [ObservableProperty] private bool _formEndRollsToNextDay;
    // The day End lands on when it rolls past midnight — worded by the View.
    [ObservableProperty] private DateTime _formEndNextDay;

    // Duration preserved across a Start edit (End shifts to keep it); re-derived whenever
    // End itself is edited. Set alongside FormStartTime/FormEndTime by SetFormTimes.
    private TimeSpan _preservedFormDuration;

    // Suppresses the shift-math reaction in OnFormStartTimeChanged/OnFormEndTimeChanged
    // for a programmatic assignment — either populating the form for an existing
    // block/draft, or the shift handler itself updating the other field — neither
    // represents a live user edit.
    private bool _isPopulatingFormTimes;

    [ObservableProperty] private bool _isFocusBlock = true;
    [ObservableProperty] private BlockPresetDto? _selectedPreset;

    // What may be done to the saved block the form is editing — the block DTO's Allowances,
    // BlockChangeChecker's answer (Gap 93). Null while the form is for a new block or a draft
    // tile, which nothing restricts. The ViewModel never works a lock out itself.
    [ObservableProperty] private BlockChangeAllowances? _editingAllowances;
    [ObservableProperty] private string? _dragError;

    [ObservableProperty] private bool _isSnapshotVisible;
    [ObservableProperty] private PlannedBlockDto? _snapshotBlock;
    [ObservableProperty] private SessionSnapshotDto? _snapshotSession;
    [ObservableProperty] private bool _isConfirmDeleteSnapshotVisible;
    [ObservableProperty] private string? _deleteSnapshotErrorMessage;

    // ── Split Block ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSplitDialogVisible;
    // The day each split piece lands on — only shown, and only used, when the block's Allowances
    // say a piece may change day (Gap 93); otherwise both pieces stay on the block's own day.
    [ObservableProperty] private DateTimeOffset _splitFirstDate;
    [ObservableProperty] private DateTimeOffset _splitSecondDate;
    public bool ShowSplitDates => EditingAllowances?.SplitPiecesMayChangeDay ?? false;

    [ObservableProperty] private TimeSpan? _splitFirstStartTime = TimeSpan.FromHours(9);
    [ObservableProperty] private TimeSpan? _splitFirstEndTime = TimeSpan.FromHours(10);
    [ObservableProperty] private TimeSpan? _splitSecondStartTime = TimeSpan.FromHours(10);
    [ObservableProperty] private TimeSpan? _splitSecondEndTime = TimeSpan.FromHours(11);
    [ObservableProperty] private string? _splitError;

    // Each piece's End dropdown shows the length each candidate time would give that piece,
    // mirroring the main form's own End field (regenerated from that piece's own Start via
    // PlannerEndTimeOptions.GenerateOffsets). Deliberately NOT restricted to land inside the
    // original block's own boundaries — the architect's explicit call: all four fields stay
    // fully independent, with SplitWarningMessage (below) surfacing any resulting mismatch
    // instead of a dropdown silently narrowing what's selectable.
    [ObservableProperty] private IReadOnlyList<TimeSpan> _splitFirstEndTimeOptions = [];
    [ObservableProperty] private IReadOnlyList<TimeSpan> _splitSecondEndTimeOptions = [];

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
    // are elsewhere in this ViewModel. Only one thing is checked live: do the two pieces
    // overlap each other (TimeSlot.Overlaps — the rule SplitPlannedBlockValidator asks too,
    // no database). Whether a piece clashes with another block, is in the past or falls
    // outside the contract's hours is answered on Confirm by the validator's placement
    // check (Gap 88 phase 8) — the ViewModel keeps no copy of those rules. Deliberately
    // does NOT check First's End against Second's Start: Split's whole point is placing
    // each piece anywhere in the day. The total-duration check is IsSplitTotalMismatched above.
    [ObservableProperty] private string? _splitWarningMessage;

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

    // Same idea for Cancel: CheckNenContractCancellableQuery asks NenContract.CheckBreak, the answer
    // the Break validator gives — hidden when the contract was sealed without cancellation or a
    // session is running. The refusal modal stays for a session started after the popover opened.
    [ObservableProperty] private bool _canCancelActiveContract;
    [ObservableProperty] private string? _completeContractErrorMessage;

    // Mirrors CanCompleteActiveContract but scoped to SelectedContract (the banner's own
    // contract) rather than the globally-Active one — see RefreshCanCompleteSelectedContractAsync.
    [ObservableProperty] private bool _canCompleteSelectedContract;

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
    // existing form/date/time mechanic rather than a bespoke calendar-drag flow. Save sends
    // CoverDeficitCommand, the ONLY path that makes a redemption block (Gap 106). The old
    // pre-create "won't fully cover — create anyway?" warning is gone: it existed only
    // because the previous flow instant-created with no chance to reconsider. Now the user
    // sees an editable form before Save, same as every other block — under-covering the
    // deficit just leaves the remainder outstanding, unremarkable like any other block.
    private bool _isCreatingRedemption;
    // The contract whose debt the tile pays, and the debt itself — the End choices stop there
    // (Gap 106, decision 19). Both set only while _isCreatingRedemption.
    private Guid? _redemptionContractId;
    private TimeSpan? _redemptionMaximum;

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

    // The banner's text is ContractRangeToBannerConverter's, from SelectedContract (Gap 89 Slice B).

    partial void OnSelectedContractChanged(NenContractRangeViewDto? value)
    {
        OnPropertyChanged(nameof(HasSelectedContract));
        OnPropertyChanged(nameof(IsContractBannerVisible));
        OnPropertyChanged(nameof(HasNoContracts));
        OnPropertyChanged(nameof(IsSelectedContractUpcoming));
        OnPropertyChanged(nameof(HasOtherContractThisWeek));
        OnPropertyChanged(nameof(SwitchContractButtonText));
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
        // The contract's own answer covers its status too (CheckComplete refuses a non-Active one, Gap 92).
        CanCompleteSelectedContract = SelectedContract is { } c &&
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
                    d.Date, d.CurrentHours, d.IsRestDay ? null : d.TargetHours, d.IsRestDay, d.Timing,
                    contract.StrictnessRule, contract.HasPeriodTarget, d.BonusHours,
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
                new GetSuggestedRedemptionPlacementsQuery(contract.Id, _clock.Now));
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
    // form never lets it change here). The user can move it and shorten it (never past the
    // debt, decision 19); Save sends CoverDeficitCommand — always one tile (Gap 106).
    private async Task OpenRedemptionCreateFormAsync(SuggestedRedemptionPlacementDto option)
    {
        await EnsurePresetsLoadedAsync();
        CoverDeficitErrorMessage = null;
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        EditingAllowances = null;
        FormTitle = "Cover Deficit";
        ResetForm();

        _isCreatingRedemption = true;
        _redemptionContractId = SelectedContract?.Id;
        _redemptionMaximum = option.Suggested.Length;
        OnPropertyChanged(nameof(CanChangePreset));
        OnPropertyChanged(nameof(ShowTypeToggle));

        IsFocusBlock = true; // redemption is Focus-only
        SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Id == option.PresetId);

        var (localStart, localEnd) = _clock.TimeZone.ToCalendarTimes(option.Suggested);
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
            // Shown as a modal the user must dismiss; the popover closes so it can't sit over it.
            IsNenContractPopoverVisible = false;
            CancelContractErrorMessage = result.ErrorMessage;
            return;
        }

        IsNenContractPopoverVisible = false;
        await RefreshContractsPopoverAsync();
        await RefreshContractsPreservingSelectionAsync();
        await LoadWeekAsync();
    }

    [RelayCommand]
    private void CancelCancelContract() => IsConfirmCancelContractVisible = false;

    [RelayCommand]
    private void DismissCancelContractError() => CancelContractErrorMessage = null;

    public string SaveButtonLabel => IsEditing ? "Save Changes" : "Create Block";

    // The form's buttons and fields read EditingAllowances (Gap 93) — the same Domain answer the
    // validators ask, so nothing is offered that will always be refused, and nothing is hidden
    // that would work.
    public bool CanDeleteFromForm => IsEditing && (EditingAllowances?.CanDelete ?? true);

    // Date and time fields: moving and resizing are one answer.
    public bool CanRescheduleBlock => EditingAllowances?.CanReschedule ?? true;

    public bool CanSaveBlock => CanRescheduleBlock || CanChangePreset;

    public bool CanDeleteSnapshotBlock => SnapshotBlock?.Allowances.CanDelete ?? false;

    public bool HasSnapshotSession => SnapshotSession is not null;

    // A Background block never has a Session (no Ritual) — the "no session was started"
    // message only makes sense for a Focus block that was Missed (or Cancelled, though
    // Cancelled blocks never reach the Planner tile list at all).
    public bool ShowSnapshotNoSessionMessage =>
        SnapshotBlock is not null && SnapshotBlock.BlockPresetType == BlockPresetType.Focus && SnapshotSession is null;

    // The snapshot header's block times as the user's calendar shows them — the ViewModel
    // asks the clock (the ViewModel Forbidden Zone: no time math here), while the DTO's
    // Shown stays a TimeSlot.
    public DateTime SnapshotStart => SnapshotBlock is null ? default : _clock.TimeZone.ToCalendarTimes(SnapshotBlock.Shown).Start;
    public DateTime SnapshotEnd => SnapshotBlock is null ? default : _clock.TimeZone.ToCalendarTimes(SnapshotBlock.Shown).End;

    // Split is offered for a saved block its contract binds (EditingAllowances.CanSplit — a new
    // block or a draft tile has no allowances, so no Split). A Cover Deficit tile is always
    // placed whole; splitting it afterwards is the ordinary Split (Gap 106, decision 4).
    public bool CanSplitBlock => EditingAllowances?.CanSplit ?? false;

    // Preset is locked to the deficit's own preset while creating a redemption block —
    // the popover already picked which preset this covers, so re-picking a different one
    // here would silently disconnect the placement from the deficit it exists to cover.
    public bool CanChangePreset => (EditingAllowances?.CanChangePreset ?? true) && !_isCreatingRedemption;

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
    // and the drafting handoff — same event-bridge shape as IRitualPrompt's
    // Changed, which ShellViewModel already consumes.
    public event Action<ContractWizardViewModel>? ContractWizardOpened;
    public event Action? ContractWizardClosed;

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(CanDeleteFromForm));
        OnPropertyChanged(nameof(ShowTypeToggle));
    }

    partial void OnEditingAllowancesChanged(BlockChangeAllowances? value)
    {
        OnPropertyChanged(nameof(CanDeleteFromForm));
        OnPropertyChanged(nameof(CanRescheduleBlock));
        OnPropertyChanged(nameof(CanChangePreset));
        OnPropertyChanged(nameof(CanSaveBlock));
        OnPropertyChanged(nameof(CanSplitBlock));
        OnPropertyChanged(nameof(ShowSplitDates));
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
        IDisplayClock displayClock,
        IActiveSessionState sessionState,
        IRecapRelay recaps,
        ISystemClock clock)
    {
        _queryDispatcher = queryDispatcher;
        _commandDispatcher = commandDispatcher;
        _displayClock = displayClock;
        _clock = clock;
        _sessionState = sessionState;
        _recaps = recaps;
        _sessionState.SessionChanged += OnSessionChanged;
        _recaps.PostSessionAcknowledged += OnPostSessionAcknowledged;

        for (int h = FirstHour; h < LastHour; h++)
            HourLabels.Add(new HourLabelViewModel(h));

        WeekStart = StartOfWeek(LocalNow.Date);
        BuildDayHeaders();

        _displayClock.MinuteTicked += OnMinuteTicked;
        UpdateCurrentTimePosition();

        _ = InitAsync();
    }

    // Each minute: move the now-line, and re-read the week's contracts so today's badge counts up
    // while a session runs — a running block's credit grows in whole minutes, so a minute is as
    // often as it can change (Gap 89 Slice A; it used to poll every 3 s with a timer of its own).
    private void OnMinuteTicked(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCurrentTimePosition();
            _ = RefreshContractsPreservingSelectionAsync();
        });
    }

    private void OnSessionChanged(object? sender, SessionDto? session)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // A block's locks come from its own Allowances (reloaded below), never from the
            // in-memory session (Gap 93, F2).
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
    // ShellViewModel surfaces it as a Post-Session popup (see IRecapRelay.
    // PostSessionsReady); this is the signal that its dismissal ("Done") is the right moment
    // to reload, since that's when the underlying data is actually known to have changed.
    private void OnPostSessionAcknowledged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => _ = LoadWeekAsync());
    }

    private void UpdateCurrentTimePosition()
    {
        var now = LocalNow;
        var weekEnd = WeekStart.AddDays(7);
        IsTodayInView = now.Date >= WeekStart.Date && now.Date < weekEnd.Date;

        if (IsTodayInView)
            CurrentTimeY = PlannerLayout.GetCurrentTimeY(now, FirstHour, HourRowHeight);

        OnPropertyChanged(nameof(TodayColumnIndex));
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
        var today = StartOfWeek(LocalNow.Date);
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
        var today = LocalNow.Date;
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
    }

    partial void OnFormStartTimeChanged(TimeSpan? value)
    {
        RegenerateEndTimeOptions();
        if (_isPopulatingFormTimes || IsDraftingContract || value is not { } newStart) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedFormDuration);
        _isPopulatingFormTimes = true;
        FormEndTime = newEnd;
        _isPopulatingFormTimes = false;

        UpdateEndRolloverDisplay(newStart, newEnd);
    }

    partial void OnFormEndTimeChanged(TimeSpan? value)
    {
        if (_isPopulatingFormTimes || IsDraftingContract || value is not { } newEnd) return;
        if (FormStartTime is not { } start) return;

        _preservedFormDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
        UpdateEndRolloverDisplay(start, newEnd);
    }

    private void UpdateEndRolloverDisplay(TimeSpan start, TimeSpan end)
    {
        FormEndRollsToNextDay = PlannerTimeShiftMath.EndRollsToNextDay(start, end);
        FormEndNextDay = FormDate.Date.AddDays(1);
    }

    /// <summary>
    /// Rebuilds the End dropdown from the current Start value and block type. While
    /// drafting a contract, entries that would roll to the next day are excluded — draft
    /// blocks keep today's same-day-only behavior (see the shift-math linkage's identical
    /// exclusion above).
    /// </summary>
    private void RegenerateEndTimeOptions() =>
        EndTimeOptions = FormStartTime is { } start
            ? EndChoices(start, PlannerEndTimeOptions.GenerateOffsets(IsFocusBlock, _redemptionMaximum), sameDayOnly: IsDraftingContract)
            : [];

    /// <summary>
    /// Split's End choices for one piece. Unlike the main form, the same-day cutoff always applies
    /// (not gated on IsDraftingContract) — the Split dialog's own stated rule is that both
    /// resulting pieces stay on the same day as the original block, unconditionally.
    /// </summary>
    private IReadOnlyList<TimeSpan> SplitEndChoices(TimeSpan? start) =>
        start is { } s ? EndChoices(s, PlannerEndTimeOptions.GenerateOffsets(IsFocusBlock), sameDayOnly: true) : [];

    /// <summary>Each offset from Start as an End time of day — stopping at midnight when the block must stay on its day.</summary>
    private static IReadOnlyList<TimeSpan> EndChoices(TimeSpan start, IReadOnlyList<TimeSpan> offsets, bool sameDayOnly)
    {
        var ends = new List<TimeSpan>();
        foreach (var offset in offsets)
        {
            var end = PlannerTimeShiftMath.ShiftEnd(start, offset);
            if (sameDayOnly && PlannerTimeShiftMath.EndRollsToNextDay(start, end))
                break;
            ends.Add(end);
        }
        return ends;
    }

    /// <summary>
    /// Recomputes the always-visible First/Second/Total duration summary,
    /// IsSplitTotalMismatched, and the non-blocking SplitWarningMessage — called
    /// unconditionally (before any _isPopulatingSplitTimes early-return) from all four
    /// Split OnXChanged handlers and once from OpenSplitDialog, so it reflects
    /// whatever's currently in the four fields regardless of whether the last change was
    /// typed or a programmatic shift-preserve.
    /// </summary>
    private void RecomputeSplitSummary()
    {
        var haveFirst = SplitFirstStartTime is not null && SplitFirstEndTime is not null;
        var haveSecond = SplitSecondStartTime is not null && SplitSecondEndTime is not null;

        SplitFirstBlockDuration = haveFirst ? PlannerTimeShiftMath.ResolvedDuration(SplitFirstStartTime!.Value, SplitFirstEndTime!.Value) : null;
        SplitSecondBlockDuration = haveSecond ? PlannerTimeShiftMath.ResolvedDuration(SplitSecondStartTime!.Value, SplitSecondEndTime!.Value) : null;
        SplitTotalDuration = haveFirst && haveSecond ? SplitFirstBlockDuration + SplitSecondBlockDuration : null;
        IsSplitTotalMismatched = SplitTotalDuration is { } total && total != SplitOriginalDuration;

        if (!haveFirst || !haveSecond)
        {
            SplitWarningMessage = null;
            return;
        }

        // Both pieces land on FormDate — the dialog has no per-piece date picker. An empty or
        // inverted piece has no slot and is reported by the form's own time checks instead.
        var dayDate = FormDate.Date;
        var first = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(dayDate.Add(SplitFirstStartTime!.Value)), _clock.TimeZone.ToMoment(dayDate.Add(SplitFirstEndTime!.Value)));
        var second = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(dayDate.Add(SplitSecondStartTime!.Value)), _clock.TimeZone.ToMoment(dayDate.Add(SplitSecondEndTime!.Value)));
        SplitWarningMessage = first is not null && second is not null && first.Overlaps(second)
            ? "First Block and Second Block overlap each other."
            : null;
    }

    /// <summary>
    /// Sets FormStartTime/FormEndTime together and (re)initializes the duration-preserving
    /// shift state from them — the single entry point every form-population site should
    /// use instead of assigning the two properties directly, so opening the form never
    /// fires the shift handlers against stale leftover values from a previous session.
    /// Whole minutes, as the fields show them: a block shortened by an early end can carry seconds.
    /// </summary>
    private void SetFormTimes(TimeSpan start, TimeSpan end)
    {
        start = PlannerTimeShiftMath.ToWholeMinutes(start);
        end = PlannerTimeShiftMath.ToWholeMinutes(end);

        _isPopulatingFormTimes = true;
        FormStartTime = start;
        FormEndTime = end;
        _isPopulatingFormTimes = false;

        // Rebuilt on every form open, not only when Start changes: the form may already hold
        // this Start (it begins at 09:00), and the drafting cutoff may have changed since (Gap 104).
        RegenerateEndTimeOptions();

        _preservedFormDuration = PlannerTimeShiftMath.ResolvedDuration(start, end);
        UpdateEndRolloverDisplay(start, end);
    }

    /// <summary>
    /// Resolves FormDate + a Start/End time-of-day pair into a TimeSlot, rolling End onto
    /// the day after FormDate when it's at or before Start — the non-draft form's single
    /// source of truth for "what does this range actually mean," shared by the redemption
    /// preview and Save so they can never resolve it differently. Null only if the pair is
    /// empty even after rollover (can't happen — rollover always yields end after start).
    /// </summary>
    private TimeSlot? ResolveFormRangeSlot(TimeSpan startTime, TimeSpan endTime)
    {
        var dayDate = FormDate.Date;
        var endDate = PlannerTimeShiftMath.EndRollsToNextDay(startTime, endTime) ? dayDate.AddDays(1) : dayDate;
        return TimeSlot.TryCreate(
            _clock.TimeZone.ToMoment(dayDate.Add(startTime)),
            _clock.TimeZone.ToMoment(endDate.Add(endTime)));
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
        var (localStart, localEnd) = _clock.TimeZone.ToCalendarTimes(dto.Shown);

        var dayStart = WeekStart.AddDays(columnIndex);
        var visibleStart = dayStart.AddHours(FirstHour);
        var visibleEnd = dayStart.AddHours(LastHour);

        var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
        if (position is null) return null;

        return new PlannedBlockTileViewModel(dto, localStart, localEnd, dto.BlockPresetType, columnIndex, position.Value.Top, position.Value.Height);
    }

    [RelayCommand]
    private async Task NewBlockAtAsync(GridSlotPosition slot)
    {
        await EnsurePresetsLoadedAsync();
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        EditingAllowances = null;
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
            EditingAllowances = null;
            FormTitle = "Edit Draft Block";
            ResetForm();

            var (draftLocalStart, draftLocalEnd) = _clock.TimeZone.ToCalendarTimes(draft.Slot);
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

        // A final block gets the read-only snapshot instead of the edit form — the block's own
        // answer (PlannedBlock.IsFinal), never worked out here (Gap 89 Slice B, A3).
        if (dto.IsFinal)
        {
            await ShowSnapshotAsync(dto);
            return;
        }

        await EnsurePresetsLoadedAsync();
        _editingBlockId = dto.Id;
        _editingIsDraft = false;
        IsEditing = true;
        EditingAllowances = dto.Allowances;
        FormTitle = "Edit Block";
        ResetForm();

        var (localStart, localEnd) = _clock.TimeZone.ToCalendarTimes(dto.Shown);
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
        OnPropertyChanged(nameof(SnapshotStart));
        OnPropertyChanged(nameof(SnapshotEnd));
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
        _redemptionContractId = null;
        _redemptionMaximum = null;
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
        if (FormStartTime is not { } startTime || FormEndTime is not { } endTime)
        {
            FormError = "Invalid time format. Use HH:mm.";
            return;
        }
        var slot = ResolveFormRangeSlot(startTime, endTime);
        if (slot is null) { FormError = "End time must be after start time."; return; }

        var blockPresetType = IsFocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;

        if (_isCreatingRedemption && _redemptionContractId is { } contractId)
        {
            // The contract decides whether the tile may pay its debt there and what it covers;
            // a wrong date comes back as its refusal (Gap 106).
            var redemption = await _commandDispatcher.DispatchAsync(new CoverDeficitCommand
            {
                ContractId = contractId,
                PresetId = SelectedPreset.Id,
                Slot = slot
            });
            if (!redemption.Success) { FormError = redemption.ErrorMessage; return; }
            if (redemption.WasCapped) DragError = RedemptionCapMessage;
        }
        else if (IsEditing && _editingBlockId.HasValue)
        {
            // A saved block keeps its type — the form hides the toggle (ShowTypeToggle), so an
            // edit is always one Update (Gap 89 Slice B, A4).
            var result = await _commandDispatcher.DispatchAsync(new UpdatePlannedBlockCommand
            {
                BlockId = _editingBlockId.Value,
                PresetId = SelectedPreset.Id,
                Slot = slot
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
        }
        else
        {
            // Manual creation never links to a deficit, even if the preset+time happens to
            // match one — only Cover Deficit (above) makes a redemption block. A new block is
            // a plain one, or a Bonus block if it falls inside a contract.
            var result = await _commandDispatcher.DispatchAsync(new CreatePlannedBlockCommand
            {
                PresetId = SelectedPreset.Id,
                BlockPresetType = blockPresetType,
                Slot = slot
            });
            if (!result.Success) { FormError = result.ErrorMessage; return; }
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
        if (FormStartTime is not { } startTime || FormEndTime is not { } endTime)
        {
            FormError = "Invalid time format. Use HH:mm.";
            return false;
        }
        if (endTime <= startTime) { FormError = "End time must be after start time."; return false; }

        var dayDate = FormDate.Date;
        var localStart = dayDate.Add(startTime);
        var localEnd = dayDate.Add(endTime);

        bool isFocus = IsFocusBlock;
        var placementError = ValidateDraftPlacement(localStart, localEnd, isFocus);
        if (placementError is not null) { FormError = placementError; return false; }

        var slot = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(localStart), _clock.TimeZone.ToMoment(localEnd));
        if (slot is null) { FormError = "End time must be after start time."; return false; }

        // No overlap check here: drafts aren't blocks yet, so "is this Focus time taken" is
        // answered once, at Seal, by BlockPlacementChecker on the whole calendar (Gap 88 phase 8).

        if (_editingIsDraft && _editingBlockId.HasValue)
        {
            var idx = _draftBlocks.FindIndex(d => d.Id == _editingBlockId.Value);
            if (idx >= 0)
                _draftBlocks[idx] = new ContractDraftBlock(_editingBlockId.Value, SelectedPreset.Id, SelectedPreset.Name, slot, isFocus);
        }
        else
        {
            _draftBlocks.Add(new ContractDraftBlock(Guid.NewGuid(), SelectedPreset.Id, SelectedPreset.Name, slot, isFocus));
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
    private void OpenSplitDialog()
    {
        if (!CanSplitBlock) return;
        if (!_editingBlockId.HasValue) return;
        if (!_blocksInWeek.TryGetValue(_editingBlockId.Value, out var dto)) return;
        var (localStart, localEnd) = _clock.TimeZone.ToCalendarTimes(dto.Shown);

        SplitError = null;
        SplitFirstDate = FormDate;
        SplitSecondDate = FormDate;
        OnPropertyChanged(nameof(ShowSplitDates));
        var midpoint = localStart + TimeSpan.FromTicks((localEnd - localStart).Ticks / 2);

        // Whole minutes, as the fields show them — an odd-minute block's midpoint has seconds.
        _isPopulatingSplitTimes = true;
        SplitFirstStartTime = PlannerTimeShiftMath.ToWholeMinutes(localStart.TimeOfDay);
        SplitFirstEndTime = PlannerTimeShiftMath.ToWholeMinutes(midpoint.TimeOfDay);
        SplitSecondStartTime = PlannerTimeShiftMath.ToWholeMinutes(midpoint.TimeOfDay);
        SplitSecondEndTime = PlannerTimeShiftMath.ToWholeMinutes(localEnd.TimeOfDay);
        _isPopulatingSplitTimes = false;

        _preservedSplitFirstDuration = midpoint - localStart;
        _preservedSplitSecondDuration = localEnd - midpoint;
        SplitOriginalDuration = localEnd - localStart;

        SplitFirstEndTimeOptions = SplitEndChoices(SplitFirstStartTime);
        SplitSecondEndTimeOptions = SplitEndChoices(SplitSecondStartTime);
        RecomputeSplitSummary();

        IsSplitDialogVisible = true;
    }

    partial void OnSplitFirstStartTimeChanged(TimeSpan? value)
    {
        SplitFirstEndTimeOptions = SplitEndChoices(value);
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes || value is not { } newStart) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedSplitFirstDuration);
        _isPopulatingSplitTimes = true;
        SplitFirstEndTime = newEnd;
        _isPopulatingSplitTimes = false;
    }

    partial void OnSplitFirstEndTimeChanged(TimeSpan? value)
    {
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes) return;
        if (SplitFirstStartTime is not { } start || value is not { } newEnd) return;

        _preservedSplitFirstDuration = PlannerTimeShiftMath.ResolvedDuration(start, newEnd);
    }

    partial void OnSplitSecondStartTimeChanged(TimeSpan? value)
    {
        SplitSecondEndTimeOptions = SplitEndChoices(value);
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes || value is not { } newStart) return;

        var newEnd = PlannerTimeShiftMath.ShiftEnd(newStart, _preservedSplitSecondDuration);
        _isPopulatingSplitTimes = true;
        SplitSecondEndTime = newEnd;
        _isPopulatingSplitTimes = false;
    }

    partial void OnSplitSecondEndTimeChanged(TimeSpan? value)
    {
        RecomputeSplitSummary();
        if (_isPopulatingSplitTimes) return;
        if (SplitSecondStartTime is not { } start || value is not { } newEnd) return;

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

        if (SplitFirstStartTime is not { } firstStart || SplitFirstEndTime is not { } firstEnd
            || SplitSecondStartTime is not { } secondStart || SplitSecondEndTime is not { } secondEnd)
        {
            SplitError = "Invalid time format. Use HH:mm.";
            return;
        }

        var firstDay = ShowSplitDates ? SplitFirstDate.Date : FormDate.Date;
        var secondDay = ShowSplitDates ? SplitSecondDate.Date : FormDate.Date;
        var firstSlot = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(firstDay.Add(firstStart)), _clock.TimeZone.ToMoment(firstDay.Add(firstEnd)));
        if (firstSlot is null) { SplitError = "First block's end time must be after its start time."; return; }
        var secondSlot = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(secondDay.Add(secondStart)), _clock.TimeZone.ToMoment(secondDay.Add(secondEnd)));
        if (secondSlot is null) { SplitError = "Second block's end time must be after its start time."; return; }

        var result = await _commandDispatcher.DispatchAsync(new SplitPlannedBlockCommand
        {
            OriginalBlockId = _editingBlockId!.Value,
            First = firstSlot,
            Second = secondSlot
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
        EditingAllowances = null;
        IsSplitDialogVisible = false;
        SplitError = null;
        _editingBlockId = null;
        _editingIsDraft = false;
    }

    // ── Drag lock checks (called from code-behind before starting a drag) ──────

    /// <summary>
    /// True when the tile must not be dragged at all — no move, no resize of either edge: the
    /// block's Allowances say it can't be rescheduled (started, or bound by a contract that
    /// allows no movement — Gap 93). Draft blocks are never locked.
    /// </summary>
    public bool IsTileFullyLocked(Guid blockId)
    {
        if (_draftBlocks.Any(d => d.Id == blockId)) return false;
        if (!_blocksInWeek.TryGetValue(blockId, out var dto)) return true;
        return !dto.Allowances.CanReschedule;
    }

    // ── Drag-create: open the form pre-filled with the dragged range ──────────

    [RelayCommand]
    private async Task OpenNewBlockFormWithRangeAsync(DragCreatedRange range)
    {
        await EnsurePresetsLoadedAsync();
        _editingBlockId = null;
        _editingIsDraft = false;
        IsEditing = false;
        EditingAllowances = null;
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
                result.NewLocalStart, result.NewLocalEnd, _draftBlocks[draftIndex].IsFocus);
            if (placementError is not null)
            {
                // No rebuild needed: _draftBlocks is untouched, and the code-behind's
                // ClearDragState() already removed the drag ghost and restored the real
                // tile's opacity before this command ran — Tiles already reflects the
                // unchanged, correct position.
                DragError = placementError;
                return;
            }

            var draftSlot = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(result.NewLocalStart), _clock.TimeZone.ToMoment(result.NewLocalEnd));
            if (draftSlot is null) { DragError = "End time must be after start time."; return; }
            _draftBlocks[draftIndex] = _draftBlocks[draftIndex] with { Slot = draftSlot };
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

        var slot = TimeSlot.TryCreate(_clock.TimeZone.ToMoment(result.NewLocalStart), _clock.TimeZone.ToMoment(result.NewLocalEnd));
        if (slot is null) { DragError = "End time must be after start time."; return; }
        var updateResult = await _commandDispatcher.DispatchAsync(new UpdatePlannedBlockCommand
        {
            BlockId   = result.BlockId,
            PresetId = dto.PresetId,
            Slot      = slot
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
            initialStartDate: DateOnly.FromDateTime(LocalNow.Date),
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
                    Guid.NewGuid(), block.PresetId, block.PresetName, block.Slot, block.IsFocus));
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
            var (localStart, localEnd) = _clock.TimeZone.ToCalendarTimes(draft.Slot);
            var dayIndex = (localStart.Date - WeekStart.Date).Days;
            if (dayIndex < 0 || dayIndex > 6) continue;

            var dayStart = WeekStart.AddDays(dayIndex);
            var visibleStart = dayStart.AddHours(FirstHour);
            var visibleEnd = dayStart.AddHours(LastHour);
            var position = PlannerLayout.GetTilePosition(localStart, localEnd, visibleStart, visibleEnd, HourRowHeight);
            if (position is null) continue;

            var blockPresetType = draft.IsFocus ? BlockPresetType.Focus : BlockPresetType.Background;
            Tiles.Add(PlannedBlockTileViewModel.ForDraft(
                draft.Id, draft.PresetName, blockPresetType, localStart, localEnd, dayIndex, position.Value.Top, position.Value.Height));
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
        if (localStart <= LocalNow)
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
            .Select(d => d.Slot)
            .ToList();

        var hoursByDay = await _queryDispatcher.QueryAsync(
            new ComputePlannedFocusHoursQuery(DraftFocusBlockRanges: focusRanges));

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
                .Select(d => new NenContractBlockSpec(d.PresetId, d.Slot, d.IsFocus))
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
                    FixedDailyFloorHours = _draftRules.WholeFixedDailyFloorHours,
                    RestDays = _draftRules.RestDays,
                    WorkHours = _draftRules.WorkHours,
                    Blocks = blocks,
                    ContractPresetId = _draftRules.ContractPresetId,
                    RedemptionPlacementPolicy = _draftRules.RedemptionPlacementPolicy,
                    KeepBlockingDuringBreaks = _draftRules.KeepBlockingDuringBreaks,
                    BonusBlocksFollowStrictness = _draftRules.BonusBlocksFollowStrictness
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
                    FixedDailyFloorHours = _draftRules.WholeFixedDailyFloorHours,
                    RestDays = _draftRules.RestDays,
                    WorkHours = _draftRules.WorkHours,
                    Blocks = blocks,
                    ContractPresetId = _draftRules.ContractPresetId,
                    RedemptionPlacementPolicy = _draftRules.RedemptionPlacementPolicy,
                    KeepBlockingDuringBreaks = _draftRules.KeepBlockingDuringBreaks,
                    BonusBlocksFollowStrictness = _draftRules.BonusBlocksFollowStrictness
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
        CanCancelActiveContract = PopoverActiveContract is not null &&
            await _queryDispatcher.QueryAsync(new CheckNenContractCancellableQuery(PopoverActiveContract.Id)) is { IsAllowed: true };
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
        _displayClock.MinuteTicked -= OnMinuteTicked;
        _sessionState.SessionChanged -= OnSessionChanged;
        _recaps.PostSessionAcknowledged -= OnPostSessionAcknowledged;
    }
}
