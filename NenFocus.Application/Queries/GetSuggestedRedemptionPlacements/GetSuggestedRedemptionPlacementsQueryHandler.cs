using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;

public sealed class GetSuggestedRedemptionPlacementsQueryHandler
    : IQueryHandler<GetSuggestedRedemptionPlacementsQuery, IReadOnlyList<SuggestedRedemptionPlacementDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly IBlockPresetRepository _presets;
    private readonly IPlannedBlockRepository _blocks;

    public GetSuggestedRedemptionPlacementsQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IBlockPresetRepository presets,
        IPlannedBlockRepository blocks)
    {
        _contracts = contracts;
        _deficits = deficits;
        _presets = presets;
        _blocks = blocks;
    }

    public async Task<IReadOnlyList<SuggestedRedemptionPlacementDto>> HandleAsync(
        GetSuggestedRedemptionPlacementsQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null) return [];

        var today = DateOnly.FromDateTime(query.AsOf);
        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(query.ContractId), ct);

        var coverableToday = deficits
            .Where(d => contract.CheckRedemptionEligibility(DateOnly.FromDateTime(d.ScheduledStart), today) == Domain.Enums.RedemptionEligibility.Allowed)
            .GroupBy(d => d.BlockPresetId)
            .ToList();

        if (coverableToday.Count == 0) return [];

        var boundary = ResolveBoundary(contract, today, query.AsOf);
        var todaysFocusBlocks = (await _blocks.GetForDayAsync(today, ct))
            .OfType<FocusBlock>()
            .Select(b => (b.Start, b.End))
            .ToList();

        var results = new List<SuggestedRedemptionPlacementDto>();
        foreach (var group in coverableToday)
        {
            var neededMinutes = group.Sum(d => d.RemainingDeficitMinutes);
            var preset = await _presets.GetByIdAsync(group.Key, ct);

            var (start, end, canFullyCover) = FindPlacement(query.AsOf, neededMinutes, boundary, todaysFocusBlocks);

            results.Add(new SuggestedRedemptionPlacementDto
            {
                PresetId = group.Key,
                PresetName = preset?.Name ?? "Unknown preset",
                TotalRemainingDeficitMinutes = neededMinutes,
                SuggestedStart = start,
                SuggestedEnd = end,
                CanFullyCover = canFullyCover
            });
        }

        return results;
    }

    /// <summary>
    /// Midnight, unless the contract has a Work Hours window configured for today — in
    /// which case that window's own close time is the binding boundary instead.
    /// </summary>
    private static DateTime ResolveBoundary(NenContract contract, DateOnly today, DateTime asOf)
    {
        var midnight = asOf.Date.AddDays(1);
        var workHours = contract.WorkHoursPolicy?.For(today);
        if (workHours is null) return midnight;

        var workHoursClose = asOf.Date.Add(workHours.End.ToTimeSpan());
        return workHoursClose < midnight ? workHoursClose : midnight;
    }

    /// <summary>
    /// Snaps forward to the next quarter-hour; shifts earlier (never before `now`) only if
    /// needed so the full duration still fits before `boundary`; searches past any
    /// conflicting block and re-checks from there. If no slot before the boundary can fit
    /// the full amount, falls back to the earliest free moment at/after `now`, covering as
    /// much as fits — CanFullyCover false signals the caller to confirm before creating it.
    /// </summary>
    private static (DateTime Start, DateTime End, bool CanFullyCover) FindPlacement(
        DateTime now, double neededMinutes, DateTime boundary, IReadOnlyList<(DateTime Start, DateTime End)> existing)
    {
        var cursor = now;

        while (true)
        {
            var snapped = SnapUpToQuarterHour(cursor);
            var latestFullCoverageStart = boundary.AddMinutes(-neededMinutes);

            DateTime candidateStart;
            bool canFullyCover;
            if (snapped <= latestFullCoverageStart)
            {
                candidateStart = snapped;
                canFullyCover = true;
            }
            else if (latestFullCoverageStart > cursor)
            {
                candidateStart = latestFullCoverageStart;
                canFullyCover = true;
            }
            else
            {
                candidateStart = cursor;
                canFullyCover = false;
            }

            var candidateEnd = canFullyCover ? candidateStart.AddMinutes(neededMinutes) : boundary;

            var conflict = existing.FirstOrDefault(b => b.Start < candidateEnd && b.End > candidateStart);
            if (conflict == default) return (candidateStart, candidateEnd, canFullyCover);

            cursor = conflict.End;
        }
    }

    private static DateTime SnapUpToQuarterHour(DateTime value)
    {
        var remainder = value.Minute % 15;
        var minutesToAdd = remainder == 0 && value.Second == 0 && value.Millisecond == 0 ? 0 : 15 - remainder;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind)
            .AddMinutes(minutesToAdd);
    }
}
