using System.Linq;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// A sealed, time-bound commitment over a contiguous date range. Does not own
/// PlannedBlocks — blocks remain their own aggregate and link back via the
/// nullable PlannedBlock.ContractId. The daily floor is a soft, tracked
/// invariant: enforcement and scoring are computed by Application-layer
/// guards that load this contract and the blocks separately.
/// </summary>
public class NenContract : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public NenContractGoal Goal { get; private set; } = null!;

    /// <summary>
    /// The contract's days — the single source of truth for its windows, floors and
    /// boundaries (Gap 65). Private: callers ask the named questions below instead.
    /// </summary>
    private ContractCalendar _calendar = null!;

    /// <summary>
    /// Mirrors of the calendar's first/last date, kept in step by every method that replaces
    /// the calendar and never set from outside — they exist so SQL range/overlap queries can
    /// filter contracts without deserializing their days.
    /// </summary>
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>
    /// EndDate exactly as sealed, frozen forever regardless of what Break later does to
    /// EndDate itself — preserves "how big was the original commitment" for history/recap
    /// purposes even after a broken contract's EndDate shrinks to its real end. Mirrors
    /// CommittedFocusHours' "frozen at Seal" idiom, applied to the calendar range instead
    /// of the hours total.
    /// </summary>
    public DateOnly OriginalEndDate { get; private set; }
    public StrictnessRule StrictnessRule { get; private set; }

    /// <summary>
    /// How this contract's days get their floor when generated — fixed, following the plan,
    /// or none. Read only when days are (re)generated (an Edit re-draft); each day's actual
    /// floor is answered by the day itself (IsRestDay / FloorHoursFor). Gap 65.
    /// </summary>
    public DailyFloorRule FloorRule { get; private set; } = null!;

    public ChangeWindow? ChangeWindow { get; private set; }
    public bool AllowCancellation { get; private set; }
    public NenContractStatus Status { get; private set; }
    public Guid? ContractPresetId { get; private set; }

    /// <summary>
    /// Whether sessions of Focus Blocks committed to this contract (PlannedBlock.ContractId
    /// matches this contract's Id) keep their preset enforced through their breaks
    /// (Gap 64). Copied by value from ContractPresetId's preset at Seal, same as every
    /// other ContractPreset-sourced field, and copied again onto each such session at
    /// Start — a running session never looks it up here. A Bonus block (ContractId ==
    /// null, even one whose Start falls inside this contract's date range) never
    /// inherits it, since it was never actually committed to the contract.
    /// </summary>
    public bool KeepBlockingDuringBreaks { get; private set; }

    /// <summary>
    /// Which days a redemption block may originate a new deficit claim on — see
    /// CheckRedemptionEligibility. Independent of StrictnessRule, which only governs
    /// whether an already-placed block can move afterward (AllowsBlockTimeChange).
    /// </summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; private set; }

    /// <summary>
    /// A whole-period minimum, as an alternative to per-day floors — mutually exclusive
    /// with any day carrying a floor, enforced
    /// in Seal. Null for every contract except a Period Time Budget contract that opted
    /// out of a per-day obligation entirely.
    /// </summary>
    public ContractFloor? ContractFloor { get; private set; }

    /// <summary>
    /// Total Focus hours actually planned at Seal — sum(plannedFocusHoursByDay), frozen
    /// forever after (there is no Extend to revisit it — see DESIGN_GAP_HISTORY.md Gap 51).
    /// This is deliberately NOT derived from the days' floors: a day can be planned above
    /// its own floor (e.g. 4h scheduled against a 1h/day fixed floor), and the true
    /// commitment is what was actually planned, not the minimum that would have passed
    /// Seal's own validation. The single source of truth for "how many hours did this
    /// contract commit to" — used for the completion grade (Open Gap L piece 2) and the
    /// Planner banner's total-floor number alike, so a later redemption or bonus block can
    /// only ever move the *achieved* side (TotalCompletedHours), never this target.
    /// </summary>
    public double CommittedFocusHours { get; private set; }

    /// <summary>User's own words on how this contract went — set via the Post-Contract
    /// recap screen, mirrors Session.Reflection exactly.</summary>
    public string? Reflection { get; private set; }

    /// <summary>Stamped when the user dismisses the Post-Contract recap screen, regardless
    /// of whether they wrote a reflection — mirrors Session.PostSessionAcknowledgedAt's
    /// idiom exactly, including the "blank is a valid, deliberate outcome" reasoning.</summary>
    public DateTime? PostContractAcknowledgedAt { get; private set; }

    private NenContract() { } // EF Core

    /// <param name="calendar">
    /// The contract's days (ContractCalendar.Generate) — windows, floors, and therefore
    /// StartsAt/EndsAt. A day with no floor is a rest day — skipped entirely by the
    /// validation loop below, not checked against zero.
    /// </param>
    /// <param name="plannedFocusHoursByDay">
    /// Focus block hours only, keyed by calendar day within the calendar's dates.
    /// Background block hours must never be included — the daily floor is a
    /// Focus-only measure by design. Days missing from the map are treated as
    /// 0 planned hours. The caller (Application layer) owns assembling this map.
    /// </param>
    /// <param name="localSealedAt">
    /// Local wall-clock time — compared against the calendar's local StartsAt.
    /// </param>
    public static NenContract Seal(
        Guid id,
        string name,
        NenContractGoal goal,
        StrictnessRule strictnessRule,
        ContractCalendar calendar,
        DailyFloorRule floorRule,
        ChangeWindow? changeWindow,
        bool allowCancellation,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay,
        DateTime localSealedAt,
        Guid? contractPresetId = null,
        ContractFloor? contractFloor = null,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract,
        bool keepBlockingDuringBreaks = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Contract name cannot be empty.", nameof(name));

        // The start may be "right now" — measured to the minute, since the current moment
        // always carries seconds and an exact comparison could never pass.
        if (calendar.StartsAt < TruncateToMinute(localSealedAt))
            throw new ArgumentException("Your start time has passed — move it to now or later.");

        if (contractFloor is not null && calendar.HasAnyDailyFloor)
            throw new ArgumentException(
                "A contract cannot have both a daily floor and a whole-contract floor.");

        if (floorRule is DailyFloorRule.None && calendar.HasAnyDailyFloor)
            throw new ArgumentException("A contract without daily floors cannot have a day with a floor.");

        foreach (var day in calendar.Days)
        {
            var floor = day.Floor;
            var date = day.Date;
            if (floor is null)
                continue; // rest day — no obligation, skipped entirely

            var plannedHours = plannedFocusHoursByDay.GetValueOrDefault(date);
            if (!floor.IsSatisfiedBy(plannedHours))
                throw new ArgumentException(
                    $"Day {date:yyyy-MM-dd} is planned below the daily floor of {floor.Hours}h (planned: {plannedHours}h).");
        }

        var contract = new NenContract
        {
            Id = id,
            Name = name.Trim(),
            Goal = goal,
            OriginalEndDate = calendar.LastDate,
            StrictnessRule = strictnessRule,
            FloorRule = floorRule,
            ChangeWindow = changeWindow,
            AllowCancellation = allowCancellation,
            ContractPresetId = contractPresetId,
            ContractFloor = contractFloor,
            RedemptionPlacementPolicy = redemptionPlacementPolicy,
            KeepBlockingDuringBreaks = keepBlockingDuringBreaks,
            CommittedFocusHours = plannedFocusHoursByDay.Values.Sum(),
            Status = calendar.StartsAt <= localSealedAt
                ? NenContractStatus.Active
                : NenContractStatus.Scheduled
        };
        contract.SetCalendar(calendar);

        contract.Raise(new NenContractSealedEvent(id, contract.Name, contract.StartDate, contract.EndDate, localSealedAt));

        return contract;
    }

    /// <summary>The only way the calendar changes — keeps the StartDate/EndDate query index in step.</summary>
    private void SetCalendar(ContractCalendar calendar)
    {
        _calendar = calendar;
        StartDate = calendar.FirstDate;
        EndDate = calendar.LastDate;
    }

    private static DateTime TruncateToMinute(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);

    /// <summary>
    /// Canonical definition of "ready to activate" — the contract's start moment has come.
    /// The lifecycle sweep asks this of every in-effect contract; nothing restates it.
    /// </summary>
    public bool IsDueForActivation(DateTime localNow)
        => Status == NenContractStatus.Scheduled && localNow >= StartsAt;

    /// <summary>
    /// Canonical definition of "currently a live commitment" — Scheduled (not yet
    /// started, but will be) or Active. Completed and Broken contracts are not in
    /// effect. The only definition: the repository fetches and asks this (Gap 101).
    /// </summary>
    public bool IsInEffect
        => Status is NenContractStatus.Scheduled or NenContractStatus.Active;

    /// <summary>
    /// Canonical definition of "ready to complete" — the contract's end moment has come.
    /// </summary>
    public bool IsDueForCompletion(DateTime localNow)
        => Status == NenContractStatus.Active && localNow >= EndsAt;

    /// <summary>
    /// The local moment this contract begins — the first day's window start: the start the
    /// user chose. Drives activation (IsDueForActivation) and Bonus-block classification.
    /// </summary>
    public DateTime StartsAt => _calendar.StartsAt;

    /// <summary>
    /// The local moment this contract ends — the last day's window end (the Work Hours end,
    /// or the end the user chose). Drives completion (IsDueForCompletion) and Bonus-block
    /// classification. Exclusive: a window ending at 24:00 is the next day's midnight.
    /// </summary>
    public DateTime EndsAt => _calendar.EndsAt;

    /// <summary>
    /// This contract's hours on the given day — the window blocks must fit inside — or null
    /// when the date is outside the contract.
    /// </summary>
    public (DateTime Start, DateTime End)? WindowOn(DateOnly date)
        => _calendar.DayOn(date) is { } day ? (day.WindowStart, day.WindowEnd) : null;

    /// <summary>
    /// Whether a local moment falls inside this contract's period — [StartsAt, EndsAt).
    /// The one definition of "does a block starting here belong to this contract's period"
    /// (Bonus classification), so no caller compares against StartsAt/EndsAt itself.
    /// </summary>
    public bool Covers(DateTime localMoment)
        => localMoment >= StartsAt && localMoment < EndsAt;

    public void Activate(DateTime activatedAt)
    {
        if (Status != NenContractStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled contract can be activated.");

        Status = NenContractStatus.Active;
        Raise(new NenContractActivatedEvent(Id, activatedAt));
    }

    public void Complete(DateTime completedAt)
    {
        if (Status != NenContractStatus.Active)
            throw new InvalidOperationException("Only an active contract can be completed.");

        Status = NenContractStatus.Completed;
        Raise(new NenContractCompletedEvent(Id, completedAt));
    }

    public void SetReflection(string? reflection)
    {
        if (Status != NenContractStatus.Completed && Status != NenContractStatus.Broken)
            throw new InvalidOperationException(
                "Reflection can only be set on a completed or broken contract.");

        Reflection = string.IsNullOrWhiteSpace(reflection)
            ? null
            : reflection.Trim();
    }

    public void MarkPostContractAcknowledged(DateTime acknowledgedAt)
    {
        if (Status != NenContractStatus.Completed && Status != NenContractStatus.Broken)
            throw new InvalidOperationException(
                "Post-contract can only be acknowledged for a completed or broken contract.");

        PostContractAcknowledgedAt = acknowledgedAt;
    }

    // AllowCancellation is deliberately not enforced here (interim, 2026-08-18) — see
    // DOMAIN_DICTIONARY.md's Nen Contract section. The real intended gate for "you can't
    // cancel this" is a future global Strict Mode Settings toggle, not this per-contract
    // flag; until Strict Mode exists, every in-effect contract can be broken regardless of
    // AllowCancellation. AllowCancellation itself is kept (still stored, still
    // user-configurable in Advanced) so Strict Mode has a real value to read once it lands.
    public void Break(DateTime localBrokenAt)
    {
        if (!IsInEffect)
            throw new InvalidOperationException("Only a contract that is still in effect (Scheduled or Active) can be broken.");

        // The days after the break day disappear — they were never going to happen, so they
        // shouldn't still "reserve" the calendar (blocking a new contract from starting there)
        // or keep attributing later bonus work to a contract that's already over. The break
        // day itself stays whole and is scored like any other day, so breaking can never
        // clean up a bad day. OriginalEndDate (set once at Seal) keeps the originally-planned
        // range for the recap. Never shrinks below the first day — breaking a Scheduled
        // contract before it started keeps its first day.
        SetCalendar(_calendar.EndingOn(DateOnly.FromDateTime(localBrokenAt)));

        Status = NenContractStatus.Broken;
        Raise(new NenContractBrokenEvent(Id, localBrokenAt));
    }

    /// <summary>
    /// A day is met if that day's floor is satisfied by its completed Focus hours. A day
    /// with no floor (a rest day, or a date outside the contract) is trivially met. Missed
    /// or late blocks do not fail the day directly — they only cost hours that must be
    /// made up elsewhere; if the hours are made up, the day is met.
    /// </summary>
    public bool IsDayMet(DateOnly date, double completedFocusHours)
    {
        var floor = FloorOn(date);
        return floor is null || floor.IsSatisfiedBy(completedFocusHours);
    }

    /// <summary>
    /// completedFocusHours as a percentage of the day's floor, uncapped (133% displays as
    /// 133%, never clamped to 100%) — null on a rest day or a floorless contract. Purely
    /// additive display concern, computed at query time — IsDayMet stays the sole
    /// authoritative pass/fail for Redemption deficit logic and lifecycle transitions.
    /// See ARCHITECT_GOVERNANCE.md Open Gap C, C1.
    /// </summary>
    public double? PercentOfFloorMet(DateOnly date, double completedFocusHours)
        => FloorOn(date)?.PercentComplete(completedFocusHours);

    /// <summary>
    /// True when this day carries no obligation at all — exposed so Application-layer
    /// callers (e.g. the active-contract banner's days-behind count) never reach into the days.
    /// </summary>
    public bool IsRestDay(DateOnly date) => FloorOn(date) is null;

    /// <summary>The day's floor in hours for display — null means rest day.</summary>
    public double? FloorHoursFor(DateOnly date) => FloorOn(date)?.Hours;

    private DailyFloor? FloorOn(DateOnly date) => _calendar.DayOn(date)?.Floor;

    /// <summary>
    /// The whole-period target's raw hour value for display — null when this contract
    /// has no ContractFloor. Exists for the same reason as FloorHoursFor: Application-
    /// layer callers never reach into ContractFloor directly.
    /// </summary>
    public double? ContractFloorHours => ContractFloor?.Hours;

    /// <summary>
    /// Whether the given total Focus hours satisfy this contract's whole-period target —
    /// trivially true when there is no ContractFloor. Mirrors IsDayMet's shape exactly,
    /// scoped to the whole period instead of one day; used both for the period-total-
    /// protection edit guard (against total currently-planned hours) and the active-
    /// contract banner (against total completed hours).
    /// </summary>
    public bool IsPeriodMet(double totalFocusHours)
        => ContractFloor is null || ContractFloor.IsSatisfiedBy(totalFocusHours);

    /// <summary>
    /// Whether a redemption block may originate a new claim against an outstanding deficit
    /// if placed on <paramref name="proposedDate"/>, given <paramref name="today"/> — governed
    /// entirely by RedemptionPlacementPolicy, never by which day the missed block was
    /// originally due (a deficit stays claimable no matter how long it's been outstanding)
    /// and never by StrictnessRule (which only governs whether an already-placed block can
    /// move afterward — see AllowsBlockTimeChange). Deliberately not consulted again once a
    /// RedemptionEntry already exists — see ComputeRedemptionAttributionQueryHandler's
    /// carry-forward pass, which rescales an existing claim's amount without re-gating
    /// whether it's still allowed to exist.
    /// </summary>
    public RedemptionEligibility CheckRedemptionEligibility(DateOnly proposedDate, DateOnly today)
    {
        return RedemptionPlacementPolicy switch
        {
            RedemptionPlacementPolicy.AnywhereInContract => proposedDate >= StartDate && proposedDate <= EndDate
                ? RedemptionEligibility.Allowed
                : RedemptionEligibility.WrongDay,
            RedemptionPlacementPolicy.CurrentDayOnly => proposedDate == today
                ? RedemptionEligibility.Allowed
                : RedemptionEligibility.WrongDay,
            _ => RedemptionEligibility.WrongDay
        };
    }

    /// <summary>
    /// Whether a block spanning [localStart, localEnd) may be placed under this contract,
    /// per each day it touches' own window — walked day-by-day so a block spanning midnight
    /// is checked against both days' windows independently. Every contract day has a
    /// window (a full day when there are no Work Hours), so day 1 before the contract's
    /// start and the last day after its end are outside. Dates outside the contract are
    /// not this method's concern. Callers must pass already-local-converted times.
    /// </summary>
    public bool AllowsBlockPlacement(DateTime localStart, DateTime localEnd)
    {
        foreach (var (date, segmentStart, segmentEnd) in DayRangeSplitter.Split(localStart, localEnd))
        {
            var day = _calendar.DayOn(date);
            if (day is null) continue;

            if (!day.Contains(segmentStart, segmentEnd))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether a block originally scheduled on <paramref name="originalDate"/> may be
    /// rescheduled to a new window spanning <paramref name="newStartDate"/> through
    /// <paramref name="newEndDate"/> (inclusive), per this contract's StrictnessRule.
    /// All three dates are calendar days — callers resolve local time before calling;
    /// this method only ever compares dates. Locked never allows a time change at all.
    /// DailyFlexibility requires the new window to stay entirely on the block's original
    /// day. PeriodTimeBudget requires the new window to stay within the contract's own
    /// [StartDate, EndDate] range.
    /// </summary>
    public bool AllowsBlockTimeChange(DateOnly originalDate, DateOnly newStartDate, DateOnly newEndDate)
    {
        if (StrictnessRule == StrictnessRule.Locked)
            return false;

        if (StrictnessRule == StrictnessRule.DailyFlexibility)
            return newStartDate == originalDate && newEndDate == originalDate;

        return newStartDate >= StartDate && newEndDate <= EndDate; // PeriodTimeBudget
    }

}
