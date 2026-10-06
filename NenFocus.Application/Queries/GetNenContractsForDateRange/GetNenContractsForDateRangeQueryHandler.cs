using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractsForDateRange;

public sealed class GetNenContractsForDateRangeQueryHandler
    : IQueryHandler<GetNenContractsForDateRangeQuery, IReadOnlyList<NenContractRangeViewDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours;
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _plannedHours;
    private readonly IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _bonusHours;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public GetNenContractsForDateRangeQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> plannedHours,
        IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> bonusHours,
        IPlannedBlockRepository blocks,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _completedHours = completedHours;
        _plannedHours = plannedHours;
        _bonusHours = bonusHours;
        _blocks = blocks;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NenContractRangeViewDto>> HandleAsync(
        GetNenContractsForDateRangeQuery query, CancellationToken cancellationToken = default)
    {
        var inEffect = await _contracts.GetAllInEffectAsync(cancellationToken);

        var candidates = inEffect
            .Where(c => c.StartDate <= query.RangeEnd && c.EndDate >= query.RangeStart)
            .OrderByDescending(c => c.Status == NenContractStatus.Active)
            .ThenBy(c => c.StartDate)
            .ToList();

        var results = new List<NenContractRangeViewDto>(candidates.Count);
        foreach (var contract in candidates)
            results.Add(await BuildViewAsync(contract, cancellationToken));

        return results;
    }

    private async Task<NenContractRangeViewDto> BuildViewAsync(NenContract contract, CancellationToken cancellationToken)
    {
        var today = _clock.TimeZone.DateOf(_clock.Now);
        var clampedToday = today < contract.StartDate ? contract.StartDate
            : today > contract.EndDate ? contract.EndDate
            : today;

        var totalDays = contract.EndDate.DayNumber - contract.StartDate.DayNumber + 1;
        var dayNumber = clampedToday.DayNumber - contract.StartDate.DayNumber + 1;

        var completedHoursByDay = await _completedHours.HandleAsync(
            new GetNenContractCompletedFocusHoursQuery(contract.Id), cancellationToken);

        var daysConsidered = 0;
        var daysBehind = 0;
        for (var date = contract.StartDate; date <= clampedToday; date = date.AddDays(1))
        {
            if (contract.IsRestDay(date)) continue;
            daysConsidered++;
            if (!contract.IsDayMet(date, completedHoursByDay.GetValueOrDefault(date)))
                daysBehind++;
        }

        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(contract.Id), cancellationToken);
        var deficitHours = deficits.Sum(d => d.RemainingDeficitMinutes) / 60.0;

        var plannedHoursByDay = await _plannedHours.HandleAsync(
            new ComputePlannedFocusHoursQuery(contract.Id, DraftFocusBlockRanges: []), cancellationToken);
        var redemptionDedupAdjustmentByDay = await ComputeRedemptionDedupAdjustmentByDayAsync(contract, cancellationToken);
        var bonusHoursByDay = await _bonusHours.HandleAsync(
            new GetNenContractBonusFocusHoursQuery(contract.Id), cancellationToken);

        // Period Time Budget using a whole-period ContractFloor has no per-day obligation
        // at all — DailyFloorPolicy is None for these contracts, so IsRestDay/IsDayMet
        // would trivially resolve "rest"/"always met" for every day. Each day's badge
        // instead compares completed-so-far against that day's own currently-planned
        // hours (a live number, not a frozen seal-time value — reflects any moves), and
        // no day is ever a rest day since none is ever individually "off."
        var isContractFloorMode = contract.ContractFloorHours.HasValue;

        var dayStatuses = new List<NenContractDayStatusDto>();
        for (var date = contract.StartDate; date <= contract.EndDate; date = date.AddDays(1))
        {
            var timing = date == today ? DayTiming.Today : date < today ? DayTiming.Past : DayTiming.Future;

            // Redemption-aware, not a raw scheduled-span sum: a redemption block that ended
            // early (or was outright missed) never trims its own End, so the raw sum would
            // keep counting its full original claim *and* whatever a later block claims to
            // cover the very same leftover — double-booking one deficit. The adjustment below
            // collapses same-day, same-missed-block claims down to that missed block's own
            // settled deficit; a retry on a *different* day is deliberately left alone (each
            // day is its own historical record of what was scheduled there, not retroactively
            // rewritten once a leftover is redeemed elsewhere).
            var plannedForDay = plannedHoursByDay.GetValueOrDefault(date)
                + redemptionDedupAdjustmentByDay.GetValueOrDefault(date);

            // Live, not a Seal-time snapshot: completedHoursByDay/plannedHoursByDay already
            // sum every currently contract-linked (committed or redemption) block's hours
            // per day, regardless of whether that day ever carried a committed floor — a
            // redemption landing on an originally-rest day, or a committed block moved
            // there under Period Time Budget, both show up here automatically.
            var currentHours = timing != DayTiming.Future
                ? completedHoursByDay.GetValueOrDefault(date)
                : plannedForDay;

            // A day with a real committed floor (DailyFloorPolicy, resolved at Seal —
            // untouched by anything below) keeps that floor pinned: floor-counting hours
            // accumulate against it and can exceed it as visible surplus, the floor value
            // itself never becomes "whatever got scheduled." A day with no committed floor
            // that has floor-counting blocks anyway gets an ad-hoc target equal to their own
            // currently-scheduled duration (plannedForDay, live) — no longer "Rest." A day
            // with neither stays "Rest." Domain (DailyFloorPolicy/IsDayMet/IsRestDay) is
            // never touched — this is purely how the read-side projection displays it.
            var committedFloor = isContractFloorMode ? null : contract.FloorHoursFor(date);
            var hasAdHocFloor = !isContractFloorMode && committedFloor is null && plannedForDay > 0;
            var isRestDay = !isContractFloorMode && committedFloor is null && !hasAdHocFloor;

            // Every "is this met" decision — real committed floor, ad-hoc floor, or Period
            // Time Budget's live period-total comparison — now funnels through the exact
            // same Domain method, DailyFloor.IsSatisfiedBy. The ad-hoc/period cases build a
            // transient DailyFloor from the live target rather than a persisted one; the
            // no-overlapping-blocks invariant CreatePlannedBlockValidator/UpdatePlannedBlockValidator
            // already enforce keeps plannedForDay within DailyFloor's 0-24h range by
            // construction — a day's own blocks can never sum past a real calendar day.
            double floorHours;
            bool isMet;
            if (isContractFloorMode)
            {
                floorHours = plannedForDay;
                isMet = new DailyFloor(plannedForDay).IsSatisfiedBy(currentHours);
            }
            else if (committedFloor is { } floor)
            {
                floorHours = floor;
                isMet = contract.IsDayMet(date, currentHours);
            }
            else if (hasAdHocFloor)
            {
                floorHours = plannedForDay;
                isMet = new DailyFloor(plannedForDay).IsSatisfiedBy(currentHours);
            }
            else
            {
                floorHours = 0;
                isMet = true; // true rest — nothing was ever owed
            }

            dayStatuses.Add(new NenContractDayStatusDto
            {
                Date = date,
                CurrentHours = currentHours,
                FloorHours = floorHours,
                IsMet = isMet,
                IsRestDay = isRestDay,
                Timing = timing,
                BonusHours = timing != DayTiming.Future ? bonusHoursByDay.GetValueOrDefault(date) : 0
            });
        }

        // The banner's numerator/denominator, uniform across every strictness rule: total
        // completed hours is always a straight sum; the target is CommittedFocusHours,
        // frozen at Seal from what was actually planned then — never derived from live
        // DailyFloorPolicy/dayStatuses data, which would double-count a redemption's
        // ad-hoc floor on top of the missed day's own floor for what's really the same
        // hour, or under-count entirely for a contract using DailyFloorPolicy.None (no
        // per-day floor at all, yet still a real originally-planned schedule). For a
        // ContractFloor-mode contract this is numerically identical to ContractFloorHours
        // (both computed the same way at Seal) — CommittedFocusHours works uniformly
        // across every strictness rule without needing that special case here.
        var totalCompletedHours = completedHoursByDay.Values.Sum();
        var totalFloorHours = contract.CommittedFocusHours;
        var totalBonusHours = bonusHoursByDay.Values.Sum();

        return new NenContractRangeViewDto
        {
            Id = contract.Id,
            Name = contract.Name,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            StrictnessRule = contract.StrictnessRule,
            Status = contract.Status,
            RedemptionPlacementPolicy = contract.RedemptionPlacementPolicy,
            DayNumber = dayNumber,
            TotalDays = totalDays,
            DaysBehind = daysBehind,
            DaysConsidered = daysConsidered,
            DeficitHours = deficitHours,
            AllowCancellation = contract.AllowCancellation,
            DayStatuses = dayStatuses,
            ContractFloorHours = contract.ContractFloorHours,
            TotalFloorHours = totalFloorHours,
            TotalCompletedHours = totalCompletedHours,
            TotalBonusHours = totalBonusHours
        };
    }

    /// <summary>
    /// Per-day correction (in hours, usually zero or negative) to subtract the raw-span
    /// double-count a stale redemption attempt leaves behind. A redemption block's own End is
    /// never trimmed once it ends early or is missed, so ComputePlannedFocusHoursQueryHandler's
    /// raw sum keeps counting its full original claim even after a later block picks up the
    /// leftover deficit. Grouped by (local day, missed block) — same-day retries against the
    /// same missed block collapse to that missed block's own settled deficit
    /// (FocusBlock.OwnDeficit); a retry on a different day is left alone, since each
    /// day's badge is its own historical record, not retroactively rewritten once a leftover
    /// is redeemed elsewhere. Grouping by the covering block's local Start date (not the
    /// day-splitting IPlannedFocusHoursCalculator ComputePlannedFocusHoursQueryHandler itself
    /// uses) is a deliberate simplification — a redemption block spanning midnight would be
    /// mis-attributed by one day, an accepted edge case given FocusBlock.MaxDurationHours and
    /// how these blocks are actually sized in practice.
    /// </summary>
    private async Task<IReadOnlyDictionary<DateOnly, double>> ComputeRedemptionDedupAdjustmentByDayAsync(
        NenContract contract, CancellationToken cancellationToken)
    {
        // Redemption blocks and the missed blocks they cover — all of them count toward the
        // floor, so a Bonus block is never part of this (Gap 97).
        var focusBlocks = (await _blocks.GetForContractAsync(contract.Id, cancellationToken))
            .OfType<FocusBlock>()
            .Where(b => b.CountsTowardFloor)
            .ToList();
        var blocksById = focusBlocks.ToDictionary(b => b.Id);
        var now = _clock.Now;

        var groups = focusBlocks
            .Where(b => b.IsRedemption)
            .SelectMany(b => b.RedemptionEntries.Select(e =>
                (Date: DateOnly.FromDateTime(_clock.TimeZone.ToCalendarTimes(b.Scheduled).Start), e.MissedBlockId, e.MinutesCovered)))
            .GroupBy(x => (x.Date, x.MissedBlockId));

        var rawMinutesByDay = new Dictionary<DateOnly, double>();
        var dedupedMinutesByDay = new Dictionary<DateOnly, double>();
        var rawDeficitCache = new Dictionary<Guid, double>();

        foreach (var group in groups)
        {
            var claimedMinutes = group.Sum(x => x.MinutesCovered);
            rawMinutesByDay[group.Key.Date] = rawMinutesByDay.GetValueOrDefault(group.Key.Date) + claimedMinutes;

            if (!rawDeficitCache.TryGetValue(group.Key.MissedBlockId, out var cap))
            {
                cap = blocksById.TryGetValue(group.Key.MissedBlockId, out var missedBlock)
                    ? missedBlock.OwnDeficit(now).TotalMinutes
                    : claimedMinutes; // missed block not found (shouldn't happen) — trust the claim, no capping
                rawDeficitCache[group.Key.MissedBlockId] = cap;
            }

            dedupedMinutesByDay[group.Key.Date] = dedupedMinutesByDay.GetValueOrDefault(group.Key.Date) + Math.Min(claimedMinutes, cap);
        }

        var adjustmentByDay = new Dictionary<DateOnly, double>();
        foreach (var date in rawMinutesByDay.Keys)
            adjustmentByDay[date] = (dedupedMinutesByDay.GetValueOrDefault(date) - rawMinutesByDay[date]) / 60.0;

        return adjustmentByDay;
    }
}
