using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeRedemptionAttribution;

public sealed class ComputeRedemptionAttributionQueryHandler
    : IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly ISystemClock _clock;

    public ComputeRedemptionAttributionQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        ISystemClock clock)
    {
        _contracts = contracts;
        _deficits = deficits;
        _clock = clock;
    }

    public async Task<RedemptionAttributionResult> HandleAsync(
        ComputeRedemptionAttributionQuery query,
        CancellationToken ct = default)
    {
        var proposedDate = DateOnly.FromDateTime(_clock.TimeZone.ToCalendarTimes(query.Block).Start);
        var today = _clock.TimeZone.DateOf(_clock.Now);
        var remainingDuration = query.Block.Length.TotalMinutes;
        var entries = new List<RedemptionEntryDto>();
        var carriedMissedBlockIds = new HashSet<Guid>();

        // Carry forward: this block's already-existing entries are continuations of an
        // already-validated commitment, not new ones — never re-gated by
        // CheckRedemptionEligibility (the day may no longer qualify, and that's fine; only
        // *new* claims are day-gated). Only their amount rescales, against the same missed
        // block they already targeted.
        if (query.ExistingEntries.Count > 0)
        {
            var linkedDeficits = await _deficits.HandleAsync(
                new GetNenContractDeficitsQuery(query.ContractId, ExcludeCoverageFromBlockId: query.ExcludeBlockId), ct);
            var byMissedBlock = linkedDeficits.ToDictionary(d => d.FocusBlockId);

            foreach (var existing in query.ExistingEntries)
            {
                if (remainingDuration <= 0) break;
                if (!byMissedBlock.TryGetValue(existing.MissedBlockId, out var deficit)) continue;

                var covered = Math.Min(remainingDuration, deficit.RemainingDeficitMinutes);
                if (covered <= 0) continue;

                entries.Add(new RedemptionEntryDto(existing.MissedBlockId, covered));
                carriedMissedBlockIds.Add(existing.MissedBlockId);
                remainingDuration -= covered;
            }
        }

        // New claims: only against capacity the carry-forward pass didn't use, and only if the
        // contract's placement policy lets a fresh claim start on this day. Only this one
        // contract is asked — which contract a block belongs to is decided by where it sits,
        // before this query runs (Gap 97); the oldest deficit is covered first.
        if (remainingDuration > 0
            && await _contracts.GetByIdAsync(query.ContractId, ct) is { } contract
            && contract.CheckRedemptionEligibility(proposedDate, today) == RedemptionEligibility.Allowed)
        {
            var deficits = await _deficits.HandleAsync(
                new GetNenContractDeficitsQuery(contract.Id, ExcludeCoverageFromBlockId: query.ExcludeBlockId), ct);

            var eligible = deficits
                .Where(d => d.BlockPresetId == query.PresetId)
                .Where(d => d.FocusBlockId != query.ExcludeBlockId)
                .Where(d => !carriedMissedBlockIds.Contains(d.FocusBlockId))
                .OrderBy(d => d.Scheduled);

            foreach (var deficit in eligible)
            {
                if (remainingDuration <= 0) break;

                var covered = Math.Min(remainingDuration, deficit.RemainingDeficitMinutes);
                if (covered <= 0) continue;

                entries.Add(new RedemptionEntryDto(deficit.FocusBlockId, covered));
                remainingDuration -= covered;
            }
        }

        // The final window: capped to what the entries actually cover (Gap 88 phase 8 — the
        // one place the cap is computed, so the validator and the handler can never disagree).
        // Empty entries cap to the requested slot unchanged.
        var redemptionEntries = entries.Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered)).ToList();
        var capped = FocusBlock.CapEndToRedemptionCoverage(query.Block, redemptionEntries);
        var wasCapped = FocusBlock.WasRedemptionCoverageMeaningfullyCapped(query.Block, capped);

        return new RedemptionAttributionResult(entries, capped, wasCapped);
    }
}
