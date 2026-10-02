using System.Linq;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// A sealed, time-bound commitment over a contiguous date range. Does not own
/// PlannedBlocks — blocks remain their own aggregate and link back via
/// PlannedBlock.Membership. The daily floor is a soft, tracked
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

    /// <summary>
    /// This contract's rules — every setting without dates, its own copy of what it was sealed
    /// with (a preset is a template, never a live source). Gap 93.
    /// </summary>
    public ContractRules Rules { get; private set; } = null!;

    public StrictnessRule StrictnessRule => Rules.Strictness;

    /// <summary>
    /// How this contract's days get their floor when generated — fixed, following the plan,
    /// or none. Read only when days are (re)generated (an Edit re-draft); each day's actual
    /// floor is answered by the day itself (IsRestDay / FloorHoursFor). Gap 65.
    /// </summary>
    public DailyFloorRule FloorRule => Rules.FloorRule;

    public ChangeWindow? ChangeWindow { get; private set; }
    public bool AllowCancellation => Rules.AllowCancellation;
    public NenContractStatus Status { get; private set; }
    public Guid? ContractPresetId { get; private set; }

    /// <summary>
    /// Whether sessions of this contract's Focus Blocks keep their preset enforced through
    /// their breaks (Gap 64). Copied onto each such session at Start — a running session never
    /// looks it up here. It applies to every member's session, Bonus blocks included (Gap 97).
    /// </summary>
    public bool KeepBlockingDuringBreaks => Rules.KeepBlockingDuringBreaks;

    /// <summary>
    /// Which days a redemption block may originate a new deficit claim on — see
    /// CheckRedemptionEligibility. Independent of StrictnessRule, which only governs
    /// whether an already-placed block can move afterward (CheckBlockMove).
    /// </summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy => Rules.RedemptionPlacement;

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
    public Moment? PostContractAcknowledgedAt { get; private set; }

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
    /// <param name="sealedAt">When the user sealed it; <paramref name="timeZone"/> gives its local
    /// time, which is compared against the calendar's local StartsAt (Gap 88: the Domain converts,
    /// the caller doesn't).</param>
    public static NenContract Seal(
        Guid id,
        string name,
        NenContractGoal goal,
        ContractRules rules,
        ContractCalendar calendar,
        ChangeWindow? changeWindow,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay,
        Moment sealedAt,
        UserTimeZone timeZone,
        Guid? contractPresetId = null,
        ContractFloor? contractFloor = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Contract name cannot be empty.", nameof(name));

        var localSealedAt = timeZone.ToCalendarTime(sealedAt);

        // The start may be "right now" — measured to the minute, since the current moment
        // always carries seconds and an exact comparison could never pass.
        if (calendar.StartsAt < TruncateToMinute(localSealedAt))
            throw new ArgumentException("Your start time has passed — move it to now or later.");

        if (contractFloor is not null && calendar.HasAnyDailyFloor)
            throw new ArgumentException(
                "A contract cannot have both a daily floor and a whole-contract floor.");

        if (rules.FloorRule is DailyFloorRule.None && calendar.HasAnyDailyFloor)
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
            // Its own copy: a stored value object belongs to one row (Gap 97's lesson).
            Rules = rules with { },
            ChangeWindow = changeWindow,
            ContractPresetId = contractPresetId,
            ContractFloor = contractFloor,
            CommittedFocusHours = plannedFocusHoursByDay.Values.Sum(),
            Status = calendar.StartsAt <= localSealedAt
                ? NenContractStatus.Active
                : NenContractStatus.Scheduled
        };
        contract.SetCalendar(calendar);

        contract.Raise(new NenContractSealedEvent(id, contract.Name, contract.StartDate, contract.EndDate, sealedAt));

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

    public void Activate(Moment activatedAt)
    {
        if (Status != NenContractStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled contract can be activated.");

        Status = NenContractStatus.Active;
        Raise(new NenContractActivatedEvent(Id, activatedAt));
    }

    public void Complete(Moment completedAt)
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

    public void MarkPostContractAcknowledged(Moment acknowledgedAt)
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
    public void Break(Moment brokenAt, UserTimeZone timeZone)
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
        SetCalendar(_calendar.EndingOn(timeZone.DateOf(brokenAt)));

        Status = NenContractStatus.Broken;
        Raise(new NenContractBrokenEvent(Id, brokenAt));
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
    /// move afterward — see CheckBlockMove). Deliberately not consulted again once a
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
    /// The planned period in real time — the first day's window start to the last day's window
    /// end. Worked out from the calendar when asked, never stored (Gap 97).
    /// </summary>
    private TimeSlot PlannedPeriod(UserTimeZone timeZone)
        => new(timeZone.ToMoment(StartsAt), timeZone.ToMoment(EndsAt));

    /// <summary>
    /// Whether a block placed at <paramref name="slot"/> is this contract's business: the
    /// contract is in effect and the slot touches its planned period. A slot entirely before or
    /// after the period — even on the first or last day — is not, and a finished contract
    /// governs nothing. The one meaning of "placed on the contract's range" (Gap 97).
    /// </summary>
    public bool Governs(TimeSlot slot, UserTimeZone timeZone)
        => IsInEffect && PlannedPeriod(timeZone).Overlaps(slot);

    /// <summary>
    /// Whether a block occupying <paramref name="slot"/> may be placed under this contract: the
    /// whole slot lies inside the planned period (one straddling its start or end doesn't), and
    /// each day it touches allows its part — cut at the user's local midnights so a block
    /// spanning midnight is checked against both days' windows independently. Every contract
    /// day has a window (a full day when there are no Work Hours). The time zone does the local
    /// conversion (Gap 88) — callers no longer convert first.
    /// </summary>
    public bool AllowsBlockPlacement(TimeSlot slot, UserTimeZone timeZone)
    {
        if (!PlannedPeriod(timeZone).Contains(slot))
            return false;

        foreach (var (date, part) in timeZone.SplitByDay(slot))
        {
            var day = _calendar.DayOn(date);
            if (day is null) return false;

            var (localStart, localEnd) = timeZone.ToCalendarTimes(part);
            if (!day.Contains(localStart, localEnd))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The membership a block placed on this contract gets: Redemption when it claims a deficit,
    /// otherwise Bonus. Committed is never handed out here — only drafting (Seal, or an Edit's
    /// re-draft) commits a block (Gap 97).
    /// </summary>
    public ContractMembership MembershipForPlacedBlock(bool claimsDeficit)
    {
        if (!IsInEffect)
            throw new InvalidOperationException("A finished contract takes no new blocks.");

        return new(Id, claimsDeficit ? ContractRole.Redemption : ContractRole.Bonus);
    }

    /// <summary>
    /// What breaking this contract does to one of its blocks (Gap 97). A Committed or Redemption
    /// block that isn't finished (<paramref name="isResolved"/> false) is cancelled; a finished
    /// one is left as it is. A Bonus block that hasn't started is the user's choice
    /// (<paramref name="keepBonusBlocks"/>): released to a plain block, or cancelled with the
    /// rest; one that has started or finished is left with its membership, so the recap's Bonus
    /// hours stay true. Whether a block is resolved needs its session, so the caller supplies it.
    /// </summary>
    public BlockFateOnBreak FateWhenBroken(PlannedBlock block, bool isResolved, Moment brokenAt, bool keepBonusBlocks)
    {
        if (block.Membership?.ContractId != Id)
            throw new ArgumentException("This block does not belong to this contract.", nameof(block));

        if (block.Membership.IsBonus)
        {
            if (block.HasStarted(brokenAt)) return BlockFateOnBreak.Leave;
            return keepBonusBlocks ? BlockFateOnBreak.Release : BlockFateOnBreak.Cancel;
        }

        return isResolved ? BlockFateOnBreak.Leave : BlockFateOnBreak.Cancel;
    }

    // ── What may happen to one of this contract's blocks (Gap 93) ───────────────────────────

    // Each question is about a block of this contract; asked about another's, it throws. A
    // finished contract's blocks are history — nothing changes and nothing is deleted — so no
    // caller needs to ask IsInEffect first.

    /// <summary>
    /// Whether this contract's rules hold the block to what was sealed: a Committed or Redemption
    /// block always; a Bonus block only when the rules say Bonus blocks follow the strictness.
    /// A block it doesn't bind is free, like a plain block — except that it never leaves the period.
    /// </summary>
    public bool Binds(PlannedBlock block)
    {
        EnsureMine(block);
        return !block.Membership!.IsBonus || Rules.BonusBlocksFollowStrictness;
    }

    /// <summary>
    /// May the block be moved or resized to <paramref name="newSlot"/>? No block of a contract
    /// ever leaves its planned period — measured in real time, so the first day before the start
    /// is outside. Inside it, a bound block moves as far as the rules' MovementRange allows; its
    /// "own day" is every day it already sits on. Whether the slot fits the day's Work Hours and
    /// is free is placement's question (BlockPlacementChecker), not this one.
    /// </summary>
    public ChangeVerdict CheckBlockMove(PlannedBlock block, TimeSlot newSlot, UserTimeZone timeZone)
    {
        EnsureMine(block);
        if (!IsInEffect)
            return ChangeVerdict.Refused(ChangeOutcome.ContractIsOver);

        if (!PlannedPeriod(timeZone).Contains(newSlot))
            return ChangeVerdict.Refused(ChangeOutcome.OutsideContractPeriod);

        if (!Binds(block))
            return ChangeVerdict.Allowed;

        switch (Rules.MovementRange)
        {
            case MovementRange.None:
                return CheckBlockMayMove(block);

            case MovementRange.OwnDay:
                var ownDays = timeZone.SplitByDay(block.Scheduled).Select(part => part.Date).ToHashSet();
                return timeZone.SplitByDay(newSlot).All(part => ownDays.Contains(part.Date))
                    ? ChangeVerdict.Allowed
                    : ChangeVerdict.Refused(ChangeOutcome.OutsideOwnDay);

            case MovementRange.WholePeriod:
                return ChangeVerdict.Allowed;

            default:
                throw new InvalidOperationException($"No movement rule for {Rules.MovementRange}.");
        }
    }

    /// <summary>
    /// May the block move at all, wherever to? No, once the contract is over, or when it binds
    /// the block and its rules allow no movement. What a screen needs before a target is chosen;
    /// <see cref="CheckBlockMove"/> answers for a concrete slot.
    /// </summary>
    public ChangeVerdict CheckBlockMayMove(PlannedBlock block)
    {
        EnsureMine(block);
        if (!IsInEffect)
            return ChangeVerdict.Refused(ChangeOutcome.ContractIsOver);

        return Binds(block) && Rules.MovementRange == MovementRange.None
            ? ChangeVerdict.Refused(ChangeOutcome.NoMovementAllowed)
            : ChangeVerdict.Allowed;
    }

    /// <summary>
    /// May the block end up on another day than its own? Only while the contract is in effect,
    /// and then for a block it doesn't bind, or where its rules let a bound block move across
    /// the whole period. What a screen needs to know before it asks for a day at all.
    /// </summary>
    public bool AllowsBlockOnAnotherDay(PlannedBlock block)
    {
        EnsureMine(block);
        return IsInEffect && (!Binds(block) || Rules.MovementRange == MovementRange.WholePeriod);
    }

    /// <summary>A bound block keeps the preset it has: deficits and redemptions are matched per preset.</summary>
    public ChangeVerdict CheckBlockPresetChange(PlannedBlock block)
    {
        EnsureMine(block);
        if (!IsInEffect)
            return ChangeVerdict.Refused(ChangeOutcome.ContractIsOver);

        return Binds(block) ? ChangeVerdict.Refused(ChangeOutcome.PresetFixed) : ChangeVerdict.Allowed;
    }

    /// <summary>
    /// Split exists to divide hours that can't be deleted and re-created, so it is offered only
    /// for a block this contract binds, and only where the rules let a block move at all. Where
    /// each half may land is <see cref="CheckBlockMove"/>'s answer.
    /// </summary>
    public ChangeVerdict CheckBlockSplit(PlannedBlock block)
    {
        EnsureMine(block);
        if (!IsInEffect)
            return ChangeVerdict.Refused(ChangeOutcome.ContractIsOver);

        return Binds(block) ? CheckBlockMayMove(block) : ChangeVerdict.Refused(ChangeOutcome.SplitNotOffered);
    }

    /// <summary>
    /// A contract's block is part of its record: only a block it doesn't bind, that hasn't
    /// started, of a contract still in effect, may be deleted — in practice a future free Bonus block.
    /// </summary>
    public ChangeVerdict CheckBlockDelete(PlannedBlock block, Moment asOf)
    {
        EnsureMine(block);
        return IsInEffect && !Binds(block) && !block.HasStarted(asOf)
            ? ChangeVerdict.Allowed
            : ChangeVerdict.Refused(ChangeOutcome.ContractBlockNeverDeleted);
    }

    /// <summary>
    /// Would the plan still meet this contract's floor? <paramref name="plannedFocusHoursByDay"/>
    /// is the planned Focus hours per day as they would be after a change (only blocks that
    /// count toward the floor). With a whole-period target the total is checked; otherwise every
    /// day that has a floor, first short day first — every day, not only the ones the change
    /// touched, so no day can be missed. The strictness plays no part: a day's floor is a day's floor.
    /// </summary>
    public ChangeVerdict CheckFloor(IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay)
    {
        if (ContractFloor is { } target)
            return target.IsSatisfiedBy(plannedFocusHoursByDay.Values.Sum())
                ? ChangeVerdict.Allowed
                : ChangeVerdict.BelowPeriodTarget(target.Hours);

        foreach (var day in _calendar.Days)
        {
            if (day.Floor is { } floor && !floor.IsSatisfiedBy(plannedFocusHoursByDay.GetValueOrDefault(day.Date)))
                return ChangeVerdict.BelowDailyFloor(day.Date, floor.Hours);
        }

        return ChangeVerdict.Allowed;
    }

    private void EnsureMine(PlannedBlock block)
    {
        if (block.Membership?.ContractId != Id)
            throw new ArgumentException("This block does not belong to this contract.", nameof(block));
    }
}
