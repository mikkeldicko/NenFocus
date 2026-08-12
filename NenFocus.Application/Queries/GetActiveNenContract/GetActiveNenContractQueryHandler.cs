using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetActiveNenContract;

public sealed class GetActiveNenContractQueryHandler : IQueryHandler<GetActiveNenContractQuery, ActiveNenContractDto?>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours;
    private readonly ISystemClock _clock;

    public GetActiveNenContractQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _completedHours = completedHours;
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

        var today = DateOnly.FromDateTime(_clock.UtcNow);
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
            AllowCancellation = contract.AllowCancellation
        };
    }
}
