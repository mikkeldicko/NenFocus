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
    private readonly ISystemClock _clock;

    public GetSuggestedRedemptionPlacementsQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        IBlockPresetRepository presets,
        IPlannedBlockRepository blocks,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _presets = presets;
        _blocks = blocks;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SuggestedRedemptionPlacementDto>> HandleAsync(
        GetSuggestedRedemptionPlacementsQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null) return [];

        // Every decision below is made in the user's wall-clock frame — the one the contract
        // speaks (its day windows and Work Hours are local, Gap 65) and the one "today" and
        // quarter-hour slots mean anything in. Translate in once here, back to UTC once at the
        // end; never compare across the two frames.
        var localNow = _clock.ToLocal(query.AsOf);
        var today = DateOnly.FromDateTime(localNow);

        // Eligibility to originate a *new* claim today no longer depends on any individual
        // deficit (missed-block date isn't even a parameter of CheckRedemptionEligibility
        // anymore) — it's a single yes/no for the whole contract, so it's checked once, not
        // per deficit. If today doesn't qualify, there's nothing to suggest at all.
        if (contract.CheckRedemptionEligibility(today, today) != Domain.Enums.RedemptionEligibility.Allowed)
            return [];

        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(query.ContractId), ct);
        var coverableToday = deficits.GroupBy(d => d.BlockPresetId).ToList();

        if (coverableToday.Count == 0) return [];

        var boundary = ResolveBoundary(contract, today, localNow);
        var todaysFocusBlocks = (await _blocks.GetForDayAsync(today, ct))
            .OfType<FocusBlock>()
            .Select(b => (_clock.ToLocal(b.Start), _clock.ToLocal(b.End)))
            .ToList();

        var results = new List<SuggestedRedemptionPlacementDto>();
        foreach (var group in coverableToday)
        {
            // Rounded up to the next whole minute — restores the guarantee Gap 25 first
            // established (accepting the suggestion as-is can only meet-or-exceed the real
            // deficit, never fall short) and closes the tick-quantization gap a raw
            // fractional duration reopened: a whole number of minutes converts to DateTime
            // ticks with zero rounding loss, so the tile this creates always fully closes
            // the deficit it was sized for, with nothing left for a tolerance to paper over.
            var neededMinutes = Math.Ceiling(group.Sum(d => d.RemainingDeficitMinutes));
            var preset = await _presets.GetByIdAsync(group.Key, ct);

            var (start, end, canFullyCover) = FindPlacement(localNow, neededMinutes, boundary, todaysFocusBlocks);

            // No room left today to place anything (e.g. "now" has already reached the
            // boundary) — offering this would create a block whose End isn't after its
            // Start, so drop the suggestion for this preset entirely rather than hand back
            // a range FocusBlock.Create can only reject.
            if (end <= start)
                continue;

            results.Add(new SuggestedRedemptionPlacementDto
            {
                PresetId = group.Key,
                PresetName = preset?.Name ?? "Unknown preset",
                TotalRemainingDeficitMinutes = neededMinutes,
                SuggestedStart = _clock.ToUtc(start),
                SuggestedEnd = _clock.ToUtc(end),
                CanFullyCover = canFullyCover
            });
        }

        return results;
    }

    /// <summary>
    /// The end of the contract's window today (its Work Hours close, its end on its last
    /// day, or midnight) — or midnight when today is outside the contract. Local, like its input.
    /// </summary>
    private static DateTime ResolveBoundary(NenContract contract, DateOnly today, DateTime localNow)
        => contract.WindowOn(today)?.End ?? localNow.Date.AddDays(1);

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
