using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetNenContractsForDateRange;

public sealed class GetNenContractsForDateRangeQueryHandler
    : IQueryHandler<GetNenContractsForDateRangeQuery, IReadOnlyList<NenContractRangeViewDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours;
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _plannedHours;
    private readonly IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _bonusHours;
    private readonly ISystemClock _clock;

    public GetNenContractsForDateRangeQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> plannedHours,
        IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> bonusHours,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _completedHours = completedHours;
        _plannedHours = plannedHours;
        _bonusHours = bonusHours;
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
        var today = _clock.ToLocalDate(_clock.UtcNow);
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
            var plannedForDay = plannedHoursByDay.GetValueOrDefault(date);

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

            double floorHours;
            bool isMet;
            if (isContractFloorMode)
            {
                floorHours = plannedForDay;
                isMet = currentHours >= plannedForDay;
            }
            else if (committedFloor is { } floor)
            {
                floorHours = floor;
                isMet = contract.IsDayMet(date, currentHours);
            }
            else if (hasAdHocFloor)
            {
                floorHours = plannedForDay;
                isMet = currentHours >= plannedForDay;
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
}
