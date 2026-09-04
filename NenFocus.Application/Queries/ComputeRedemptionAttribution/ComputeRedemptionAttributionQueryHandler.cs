using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

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
        var proposedDate = _clock.ToLocalDate(query.BlockStart);
        var today = _clock.ToLocalDate(_clock.UtcNow);
        var remainingDuration = (query.BlockEnd - query.BlockStart).TotalMinutes;
        var entries = new List<RedemptionEntryDto>();
        var carriedMissedBlockIds = new HashSet<Guid>();

        // Carry forward: this block's already-existing entries are continuations of an
        // already-validated commitment, not new ones — never re-gated by
        // CheckRedemptionEligibility (the day may no longer qualify, and that's fine; only
        // *new* claims are day-gated). Only their amount rescales, against the same missed
        // block they already targeted. Only reachable with an explicit ContractId — an
        // already-linked block always has one; auto-discovery never carries entries.
        if (query.ContractId is { } linkedContractId && query.ExistingEntries.Count > 0)
        {
            var linkedDeficits = await _deficits.HandleAsync(
                new GetNenContractDeficitsQuery(linkedContractId, ExcludeCoverageFromBlockId: query.ExcludeBlockId), ct);
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

        Guid? newClaimContractId = null;

        // New claims: only against capacity the carry-forward pass didn't use, and only
        // for a contract/day currently eligible to originate a fresh commitment.
        if (remainingDuration > 0)
        {
            var candidates = query.ContractId is { } contractId
                ? await _contracts.GetByIdAsync(contractId, ct) is { } scoped ? [scoped] : []
                : await _contracts.GetAllInEffectAsync(ct);

            var eligible = new List<(NenContract Contract, FocusBlockDeficitDto Deficit)>();
            foreach (var contract in candidates)
            {
                if (contract.CheckRedemptionEligibility(proposedDate, today) != RedemptionEligibility.Allowed)
                    continue;

                var deficits = await _deficits.HandleAsync(
                    new GetNenContractDeficitsQuery(contract.Id, ExcludeCoverageFromBlockId: query.ExcludeBlockId), ct);
                foreach (var deficit in deficits)
                {
                    if (deficit.BlockPresetId != query.PresetId) continue;
                    if (deficit.FocusBlockId == query.ExcludeBlockId) continue;
                    if (carriedMissedBlockIds.Contains(deficit.FocusBlockId)) continue;

                    eligible.Add((contract, deficit));
                }
            }

            if (eligible.Count > 0)
            {
                // Every entry a single block ends up carrying must belong to one contract —
                // PlannedBlock.ContractId is a single value. The oldest eligible deficit
                // across every candidate decides which contract a new claim attaches to;
                // only that contract's deficits are considered for the spillover walk below.
                var resolvedContractId = query.ContractId
                    ?? eligible.OrderBy(e => e.Deficit.ScheduledStart).First().Contract.Id;
                var ordered = eligible
                    .Where(e => e.Contract.Id == resolvedContractId)
                    .OrderBy(e => e.Deficit.ScheduledStart)
                    .ToList();

                foreach (var (_, deficit) in ordered)
                {
                    if (remainingDuration <= 0) break;

                    var covered = Math.Min(remainingDuration, deficit.RemainingDeficitMinutes);
                    if (covered <= 0) continue;

                    entries.Add(new RedemptionEntryDto(deficit.FocusBlockId, covered));
                    remainingDuration -= covered;
                }

                if (entries.Count > 0) newClaimContractId = resolvedContractId;
            }
        }

        // No entries at all (neither carried forward nor newly claimed): an explicit
        // query.ContractId is echoed back as-is (this block still belongs to that
        // contract, it just doesn't happen to cover anything right now); auto-discovery
        // (null) stays null, since there's no reason to attach an ordinary new block to
        // any contract at all.
        if (entries.Count == 0)
            return new RedemptionAttributionResult(query.ContractId, []);

        return new RedemptionAttributionResult(query.ContractId ?? newClaimContractId, entries);
    }
}
