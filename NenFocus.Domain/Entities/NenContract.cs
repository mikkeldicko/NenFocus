using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
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
    public StrictnessRule StrictnessRule { get; private set; }
    public DailyFloorPolicy DailyFloorPolicy { get; private set; } = null!;
    public ChangeWindow? ChangeWindow { get; private set; }
    public bool AllowCancellation { get; private set; }
    public NenContractStatus Status { get; private set; }
    public WorkHoursPolicy? WorkHoursPolicy { get; private set; }
    public Guid? ContractPresetId { get; private set; }

    /// <summary>
    /// A whole-period minimum, as an alternative to DailyFloorPolicy's per-day
    /// resolution — mutually exclusive with a real (non-empty) DailyFloorPolicy, enforced
    /// in Seal. Null for every contract except a Period Time Budget contract that opted
    /// out of a per-day obligation entirely.
    /// </summary>
    public ContractFloor? ContractFloor { get; private set; }

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
        ContractFloor? contractFloor = null)
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
            StrictnessRule = strictnessRule,
            DailyFloorPolicy = dailyFloorPolicy,
            ChangeWindow = changeWindow,
            AllowCancellation = allowCancellation,
            WorkHoursPolicy = workHoursPolicy,
            ContractPresetId = contractPresetId,
            ContractFloor = contractFloor,
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
    /// Whether a missed Focus block due on <paramref name="missedBlockDate"/> can be
    /// covered by a make-up block on <paramref name="proposedDate"/>, per this contract's
    /// StrictnessRule. Locked never allows it. DailyFlexibility requires same-day
    /// coverage — its whole model is same-day rearrangement. PeriodTimeBudget has no
    /// day constraint; only the remaining-deficit amount matters, which callers check
    /// separately.
    /// </summary>
    public MakeUpEligibility CheckMakeUpEligibility(DateOnly missedBlockDate, DateOnly proposedDate)
    {
        if (StrictnessRule == StrictnessRule.Locked)
            return MakeUpEligibility.NotAllowed;

        if (StrictnessRule == StrictnessRule.DailyFlexibility)
            return proposedDate == missedBlockDate ? MakeUpEligibility.Allowed : MakeUpEligibility.WrongDay;

        return MakeUpEligibility.Allowed; // PeriodTimeBudget
    }

    /// <summary>
    /// Whether a block spanning [start, end) may be placed under this contract, per its
    /// Work Hours window for that day. A contract with no WorkHoursPolicy configured
    /// (Simple contracts, or Advanced contracts that opted out) has no constraint.
    /// </summary>
    public bool AllowsBlockPlacement(DateOnly date, DateTime start, DateTime end)
    {
        var workHours = WorkHoursPolicy?.For(date);
        return workHours is null || workHours.ContainsRange(start, end);
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

    /// <param name="plannedFocusHoursByNewDays">
    /// Focus block hours only, keyed by calendar day within (current EndDate, newEndDate].
    /// Only the new days are floor-checked — days already covered by the original Seal
    /// are not re-validated. Days missing from the map are treated as 0 planned hours.
    /// </param>
    /// <param name="dailyFloorOverridesForNewDays">
    /// Per-day floor exceptions for the newly added days only, merged into the existing
    /// DailyFloorPolicy's PerDayExceptions. A day not present here falls back to the
    /// policy's Default (or is a rest day if there is no Default). Required for Simple
    /// contracts, whose floor always varies per day; Advanced contracts extending with
    /// their existing fixed Default typically pass an empty map.
    /// </param>
    public void Extend(
        DateOnly newEndDate,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByNewDays,
        DateTime extendedAt,
        IReadOnlyDictionary<DateOnly, DailyFloor?>? dailyFloorOverridesForNewDays = null)
    {
        if (Status != NenContractStatus.Active)
            throw new InvalidOperationException("Only an active contract can be extended.");

        if (newEndDate <= EndDate)
            throw new ArgumentException("New end date must be after the current end date.");

        var overrides = dailyFloorOverridesForNewDays ?? new Dictionary<DateOnly, DailyFloor?>();

        for (var date = EndDate.AddDays(1); date <= newEndDate; date = date.AddDays(1))
        {
            var floor = overrides.TryGetValue(date, out var overrideFloor) ? overrideFloor : DailyFloorPolicy.Default;
            if (floor is null)
                continue; // rest day — no obligation, skipped entirely

            var plannedHours = plannedFocusHoursByNewDays.GetValueOrDefault(date);
            if (!floor.IsSatisfiedBy(plannedHours))
                throw new ArgumentException(
                    $"Day {date:yyyy-MM-dd} is planned below the daily floor of {floor.Hours}h (planned: {plannedHours}h).");
        }

        if (overrides.Count > 0)
        {
            var mergedExceptions = new Dictionary<DateOnly, DailyFloor?>(DailyFloorPolicy.PerDayExceptions);
            foreach (var (date, floor) in overrides)
                mergedExceptions[date] = floor;

            DailyFloorPolicy = new DailyFloorPolicy(DailyFloorPolicy.Default, mergedExceptions);
        }

        var oldEndDate = EndDate;
        EndDate = newEndDate;
        Raise(new NenContractExtendedEvent(Id, oldEndDate, newEndDate, extendedAt));
    }
}
