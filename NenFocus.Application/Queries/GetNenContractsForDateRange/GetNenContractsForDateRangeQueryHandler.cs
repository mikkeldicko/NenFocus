using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDayStatuses;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractsForDateRange;

public sealed class GetNenContractsForDateRangeQueryHandler
    : IQueryHandler<GetNenContractsForDateRangeQuery, IReadOnlyList<NenContractRangeViewDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>> _dayStatuses;
    private readonly ISystemClock _clock;

    public GetNenContractsForDateRangeQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>> dayStatuses,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _dayStatuses = dayStatuses;
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

        // The one day rule (NenContract.DayStatuses, Gap 95): the badges, the days-behind count and
        // the banner's totals all read the same days, so they can't disagree.
        var days = await _dayStatuses.HandleAsync(new GetNenContractDayStatusesQuery(contract.Id), cancellationToken);

        var daysConsidered = days.Count(d => d.Date <= clampedToday && !d.IsRest);
        var daysBehind = days.Count(d => d.Date <= clampedToday && !d.IsRest && !d.IsMet);

        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(contract.Id), cancellationToken);
        var deficit = deficits.Aggregate(WholeMinutes.Zero, (sum, d) => sum + d.RemainingDeficit);

        var dayStatuses = days.Select(d =>
        {
            var timing = d.Date == today ? DayTiming.Today : d.Date < today ? DayTiming.Past : DayTiming.Future;
            var future = timing == DayTiming.Future;
            return new NenContractDayStatusDto
            {
                Date = d.Date,
                // A future day shows its plan: by the one rule the plan is exactly what the day
                // owes, so it is met as planned. A day under way or past shows what was done.
                CurrentHours = future ? d.TargetHours : d.CompletedHours,
                TargetHours = d.TargetHours,
                IsMet = future || d.IsMet,
                IsRestDay = d.IsRest,
                Timing = timing,
                BonusHours = future ? 0 : d.BonusHours
            };
        }).ToList();

        // The banner: everything completed against everything committed at Seal.
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
            Deficit = deficit,
            AllowCancellation = contract.AllowCancellation,
            DayStatuses = dayStatuses,
            HasPeriodTarget = contract.HasPeriodTarget,
            TotalCommittedHours = contract.CommittedFocusHours,
            TotalCompletedHours = days.Sum(d => d.CompletedHours),
            TotalBonusHours = days.Sum(d => d.BonusHours)
        };
    }
}
