using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;

public sealed class GetSuggestedRedemptionPlacementsQueryHandler
    : IQueryHandler<GetSuggestedRedemptionPlacementsQuery, IReadOnlyList<SuggestedRedemptionPlacementDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public GetSuggestedRedemptionPlacementsQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _presets = presets;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SuggestedRedemptionPlacementDto>> HandleAsync(
        GetSuggestedRedemptionPlacementsQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null) return [];

        var localNow = _clock.TimeZone.ToCalendarTime(query.AsOf);
        var today = DateOnly.FromDateTime(localNow);

        // Eligibility to originate a *new* claim today is a single yes/no for the whole
        // contract, checked once. If today doesn't qualify, there's nothing to suggest at all.
        if (contract.CheckRedemptionEligibility(today, today) != RedemptionEligibility.Allowed)
            return [];

        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(query.ContractId), ct);
        var coverableToday = deficits.GroupBy(d => d.BlockPresetId).ToList();
        if (coverableToday.Count == 0) return [];

        // The suggestion only pre-fills the block form — it isn't a placement decision (Gap 88,
        // decided 2026-09-29). Start at the next quarter hour from now, sized exactly to the deficit
        // (whole minutes, Gap 92 V10 — accepting the suggestion as-is pays it exactly). Whether
        // it overlaps something or falls outside the contract is Save's job
        // (BlockPlacementChecker.Check).
        var start = _clock.TimeZone.ToMoment(SnapUpToQuarterHour(localNow));

        var results = new List<SuggestedRedemptionPlacementDto>();
        foreach (var group in coverableToday)
        {
            var remaining = group.Aggregate(WholeMinutes.Zero, (sum, d) => sum + d.RemainingDeficit);
            var preset = await _presets.GetByIdAsync(group.Key, ct);

            results.Add(new SuggestedRedemptionPlacementDto
            {
                PresetId = group.Key,
                PresetName = preset?.Name ?? "Unknown preset",
                TotalRemainingDeficit = remaining,
                Suggested = TimeSlot.Covering(start, remaining)
            });
        }

        return results;
    }

    private static DateTime SnapUpToQuarterHour(DateTime value)
    {
        var remainder = value.Minute % 15;
        var minutesToAdd = remainder == 0 && value.Second == 0 && value.Millisecond == 0 ? 0 : 15 - remainder;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind)
            .AddMinutes(minutesToAdd);
    }
}
