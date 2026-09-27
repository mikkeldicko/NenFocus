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
    public DailyFloorPolicy DailyFloorPolicy { get; private set; } = null!;
    public ChangeWindow? ChangeWindow { get; private set; }
    public bool AllowCancellation { get; private set; }
    public NenContractStatus Status { get; private set; }
    public WorkHoursPolicy? WorkHoursPolicy { get; private set; }
    public Guid? ContractPresetId { get; private set; }

    /// <summary>
    /// Whether a Focus Block committed to this contract (PlannedBlock.ContractId
    /// matches this contract's Id) should stop enforcing while its Session is
    /// inside a break slot. Copied by value from ContractPresetId's preset at Seal, same as
    /// every other ContractPreset-sourced field. Resolved live by
    /// IPolicyEngine.ComputeSlice on every tick, never locked in at Seal — so a
    /// later global Settings change is picked up immediately rather than leaving
    /// already-sealed contracts stuck with stale behavior. Can only ever narrow
    /// the global default toward stricter (keep blocking), never loosen it. A
    /// Bonus block (ContractId == null, even one whose Start falls inside this
    /// contract's date range) never reads this — it follows the global default
    /// alone, since it was never actually committed to the contract.
    /// </summary>
    public bool PauseEnforcementDuringBreak { get; private set; }

    /// <summary>
    /// Which days a redemption block may originate a new deficit claim on — see
    /// CheckRedemptionEligibility. Independent of StrictnessRule, which only governs
    /// whether an already-placed block can move afterward (AllowsBlockTimeChange).
    /// </summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; private set; }

    /// <summary>
    /// A whole-period minimum, as an alternative to DailyFloorPolicy's per-day
    /// resolution — mutually exclusive with a real (non-empty) DailyFloorPolicy, enforced
    /// in Seal. Null for every contract except a Period Time Budget contract that opted
    /// out of a per-day obligation entirely.
    /// </summary>
    public ContractFloor? ContractFloor { get; private set; }

    /// <summary>
    /// Total Focus hours actually planned at Seal — sum(plannedFocusHoursByDay), frozen
    /// forever after (there is no Extend to revisit it — see DESIGN_GAP_HISTORY.md Gap 51).
    /// This is deliberately NOT derived from DailyFloorPolicy: a day can be planned above
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

    /// <param name="dailyFloorPolicy">
    /// Resolves the required daily Focus hours per day. A day whose resolved floor is
    /// null (no per-day exception and no default) is a rest day — skipped entirely by
    /// the validation loop below, not checked against zero.
    /// </param>
    /// <param name="plannedFocusHoursByDay">
    /// Focus block hours only, keyed by calendar day within [startDate, endDate].
    /// Background block hours must never be included — the daily floor is a
    /// Focus-only measure by design. Days missing from the map are treated as
    /// 0 planned hours. The caller (Application layer) owns assembling this map.
    /// </param>
    public static NenContract Seal(
        Guid id,
        string name,
        NenContractGoal goal,
        DateOnly startDate,
        DateOnly endDate,
        StrictnessRule strictnessRule,
        DailyFloorPolicy dailyFloorPolicy,
        ChangeWindow? changeWindow,
        bool allowCancellation,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay,
        DateTime sealedAt,
        WorkHoursPolicy? workHoursPolicy = null,
        Guid? contractPresetId = null,
        ContractFloor? contractFloor = null,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract,
        bool pauseEnforcementDuringBreak = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Contract name cannot be empty.", nameof(name));

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after the start date.");

        if (contractFloor is not null && HasAnyRealFloor(dailyFloorPolicy))
            throw new ArgumentException(
                "A contract cannot have both a daily floor and a whole-contract floor.");

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var floor = dailyFloorPolicy.For(date);
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
            StartDate = startDate,
            EndDate = endDate,
            OriginalEndDate = endDate,
            StrictnessRule = strictnessRule,
            DailyFloorPolicy = dailyFloorPolicy,
            ChangeWindow = changeWindow,
            AllowCancellation = allowCancellation,
            WorkHoursPolicy = workHoursPolicy,
            ContractPresetId = contractPresetId,
            ContractFloor = contractFloor,
            RedemptionPlacementPolicy = redemptionPlacementPolicy,
            PauseEnforcementDuringBreak = pauseEnforcementDuringBreak,
            CommittedFocusHours = plannedFocusHoursByDay.Values.Sum(),
            Status = DateOnly.FromDateTime(sealedAt) >= startDate
                ? NenContractStatus.Active
                : NenContractStatus.Scheduled
        };

        contract.Raise(new NenContractSealedEvent(id, contract.Name, startDate, endDate, sealedAt));

        return contract;
    }

    /// <summary>True when the policy carries a real obligation somewhere — a Default, or
    /// any non-null per-day exception — as opposed to DailyFloorPolicy.None, which
    /// carries neither and is the only shape allowed alongside a ContractFloor.</summary>
    private static bool HasAnyRealFloor(DailyFloorPolicy policy)
        => policy.Default is not null || policy.PerDayExceptions.Values.Any(floor => floor is not null);

    /// <summary>
    /// Canonical definition of "ready to activate" — the repository query behind the
    /// lifecycle heartbeat must return exactly the contracts this returns true for.
    /// </summary>
    public bool IsDueForActivation(DateOnly asOf)
        => Status == NenContractStatus.Scheduled && asOf >= StartDate;

    /// <summary>
    /// Canonical definition of "currently a live commitment" — Scheduled (not yet
    /// started, but will be) or Active. Completed and Broken contracts are not in
    /// effect. The repository query behind ContractPreset deletion protection must
    /// match this exactly, same discipline as IsDueForActivation/IsDueForCompletion.
    /// </summary>
    public bool IsInEffect
        => Status is NenContractStatus.Scheduled or NenContractStatus.Active;

    /// <summary>
    /// Canonical definition of "ready to complete" — EndDate is inclusive, so a contract
    /// is only due once asOf is strictly after it.
    /// </summary>
    public bool IsDueForCompletion(DateOnly asOf)
        => Status == NenContractStatus.Active && asOf > EndDate;

    /// <summary>
    /// The true instant this contract's commitment begins — StartDate at midnight unless
    /// a Work Hours window covers that day, in which case it's that window's own start.
    /// Deliberately does not delay Activate()/IsDueForActivation (still fires at midnight
    /// on StartDate, unchanged) — this only affects Bonus-block classification, not the
    /// Scheduled-to-Active transition. See ARCHITECT_GOVERNANCE.md Open Gap L.
    /// </summary>
    public DateTime StartsAt =>
        WorkHoursPolicy?.For(StartDate) is { } hours
            ? StartDate.ToDateTime(hours.Start)
            : StartDate.ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// The true instant this contract's commitment ends — EndDate at the end of the day
    /// unless a Work Hours window covers that day, in which case it's that window's own
    /// end. Governs both passive completion timing (IsDueForCompletion) and Bonus-block
    /// classification. See ARCHITECT_GOVERNANCE.md Open Gap L.
    /// </summary>
    public DateTime EndsAt =>
        WorkHoursPolicy?.For(EndDate) is { } hours
            ? EndDate.ToDateTime(hours.End)
            : EndDate.ToDateTime(TimeOnly.MaxValue);

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
    public void Break(DateTime brokenAt)
    {
        if (!IsInEffect)
            throw new InvalidOperationException("Only a contract that is still in effect (Scheduled or Active) can be broken.");

        // EndDate shrinks to reflect what actually happened — the days after this point were
        // never going to happen, so they shouldn't still "reserve" the calendar (blocking a
        // new contract from starting there) or keep attributing later bonus work to a
        // contract that's already over. OriginalEndDate (set once at Seal, above) keeps the
        // originally-planned range for history/recap purposes regardless. Clamped to never
        // go below StartDate — breaking a Scheduled contract before it ever started must not
        // produce EndDate < StartDate, which would violate Seal's own invariant.
        var brokenDate = DateOnly.FromDateTime(brokenAt);
        if (brokenDate < EndDate) EndDate = brokenDate;
        if (EndDate < StartDate) EndDate = StartDate;

        Status = NenContractStatus.Broken;
        Raise(new NenContractBrokenEvent(Id, brokenAt));
    }

    /// <summary>
    /// A day is met if that day's resolved floor is satisfied by its completed Focus
    /// hours. A day with no resolved floor (a rest day) is trivially met. Missed or
    /// late blocks do not fail the day directly — they only cost hours that must be
    /// made up elsewhere; if the hours are made up, the day is met.
    /// </summary>
    public bool IsDayMet(DateOnly date, double completedFocusHours)
    {
        var floor = DailyFloorPolicy.For(date);
        return floor is null || floor.IsSatisfiedBy(completedFocusHours);
    }

    /// <summary>
    /// completedFocusHours as a percentage of the day's resolved floor, uncapped (133%
    /// displays as 133%, never clamped to 100%) — null on a rest day (no resolved floor)
    /// or a floorless contract, mirroring IsDayMet's own delegation to DailyFloorPolicy.
    /// Purely additive display concern, computed at query time — IsDayMet stays the sole
    /// authoritative pass/fail for Redemption deficit logic and lifecycle transitions.
    /// See ARCHITECT_GOVERNANCE.md Open Gap C, C1.
    /// </summary>
    public double? PercentOfFloorMet(DateOnly date, double completedFocusHours)
        => DailyFloorPolicy.For(date)?.PercentComplete(completedFocusHours);

    /// <summary>
    /// True when this day resolves to no obligation at all — same resolution IsDayMet
    /// uses internally, exposed so Application-layer callers (e.g. the active-contract
    /// banner's days-behind count) never reach into DailyFloorPolicy directly.
    /// </summary>
    public bool IsRestDay(DateOnly date) => DailyFloorPolicy.For(date) is null;

    /// <summary>
    /// The resolved floor's raw hour value for display — null means rest day. Exists
    /// for the same reason as IsRestDay: Application-layer callers never reach into
    /// DailyFloorPolicy directly.
    /// </summary>
    public double? FloorHoursFor(DateOnly date) => DailyFloorPolicy.For(date)?.Hours;

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
    /// per each day it touches' own Work Hours window — walked day-by-day so a block
    /// spanning midnight is checked against both days' windows independently, rather than
    /// anchored to a single day. A contract with no WorkHoursPolicy configured (Simple
    /// contracts, or Advanced contracts that opted out) has no constraint. Callers must
    /// pass already-local-converted times — Work Hours is configured in the user's local
    /// wall-clock terms, and this method has no way to convert on its own.
    /// </summary>
    public bool AllowsBlockPlacement(DateTime localStart, DateTime localEnd)
    {
        foreach (var (date, segmentStart, segmentEnd) in DayRangeSplitter.Split(localStart, localEnd))
        {
            var workHours = WorkHoursPolicy?.For(date);
            if (workHours is null) continue;

            // DayRangeSplitter ends every non-final day-segment exactly at midnight — but
            // TimeOnly (WorkHours.End's type) can never represent "24:00", so a segment
            // ending exactly at midnight could never satisfy any window otherwise, no
            // matter how late it's configured. Same "exact midnight has no presence on the
            // next day" resolution already used for date extraction elsewhere in this
            // codebase (e.g. SplitPlannedBlockValidator), applied here to the time check.
            var effectiveEnd = segmentEnd.TimeOfDay == TimeSpan.Zero ? segmentEnd.AddTicks(-1) : segmentEnd;
            if (!workHours.ContainsRange(segmentStart, effectiveEnd))
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
