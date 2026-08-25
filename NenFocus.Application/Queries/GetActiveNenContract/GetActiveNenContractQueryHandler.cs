using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetActiveNenContract;

public sealed class GetActiveNenContractQueryHandler : IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours;
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _plannedHours;
    private readonly ISystemClock _clock;

    public GetActiveNenContractQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> plannedHours,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _completedHours = completedHours;
        _plannedHours = plannedHours;
        _clock = clock;
    }

    public async Task<ActiveNenContractDto?> HandleAsync(GetActiveNenContractQuery query, CancellationToken cancellationToken = default)
    {
        var inEffect = await _contracts.GetAllInEffectAsync(cancellationToken);

        var contract = inEffect
            .Where(c => c.StartDate <= query.WeekEnd && c.EndDate >= query.WeekStart)
            .OrderByDescending(c => c.Status == NenContractStatus.Active)
            .ThenBy(c => c.StartDate)
            .FirstOrDefault();

        if (contract is null)
            return null;

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
            var isRestDay = !isContractFloorMode && contract.IsRestDay(date);
            var plannedForDay = plannedHoursByDay.GetValueOrDefault(date);
            var currentHours = isRestDay ? 0
                : timing != DayTiming.Future ? completedHoursByDay.GetValueOrDefault(date)
                : plannedForDay;

            dayStatuses.Add(new NenContractDayStatusDto
            {
                Date = date,
                CurrentHours = currentHours,
                FloorHours = isContractFloorMode ? plannedForDay : contract.FloorHoursFor(date) ?? 0,
                IsMet = isContractFloorMode ? currentHours >= plannedForDay : contract.IsDayMet(date, currentHours),
                IsRestDay = isRestDay,
                Timing = timing
            });
        }

        // The banner's numerator/denominator, uniform across every strictness rule: total
        // completed hours is always a straight sum; the target is either this contract's
        // ContractFloor or the sum of every day's resolved floor (dayStatuses.FloorHours
        // already holds exactly that per-day number for a non-ContractFloor contract).
        var totalCompletedHours = completedHoursByDay.Values.Sum();
        var totalFloorHours = contract.ContractFloorHours ?? dayStatuses.Sum(d => d.FloorHours);

        return new ActiveNenContractDto
        {
            Id = contract.Id,
            Name = contract.Name,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            StrictnessRule = contract.StrictnessRule,
            DayNumber = dayNumber,
            TotalDays = totalDays,
            DaysBehind = daysBehind,
            DaysConsidered = daysConsidered,
            DeficitHours = deficitHours,
            AllowCancellation = contract.AllowCancellation,
            DayStatuses = dayStatuses,
            ContractFloorHours = contract.ContractFloorHours,
            TotalFloorHours = totalFloorHours,
            TotalCompletedHours = totalCompletedHours
        };
    }
}
