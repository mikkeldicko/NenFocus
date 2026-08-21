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

    public ComputeRedemptionAttributionQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits)
    {
        _contracts = contracts;
        _deficits = deficits;
    }

    public async Task<RedemptionAttributionResult> HandleAsync(
        ComputeRedemptionAttributionQuery query,
        CancellationToken ct = default)
    {
        var candidates = query.ContractId is { } contractId
            ? await _contracts.GetByIdAsync(contractId, ct) is { } scoped ? [scoped] : []
            : await _contracts.GetAllInEffectAsync(ct);

        var proposedDate = DateOnly.FromDateTime(query.BlockStart);
        var eligible = new List<(NenContract Contract, FocusBlockDeficitDto Deficit)>();

        foreach (var contract in candidates)
        {
            var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(contract.Id), ct);
            foreach (var deficit in deficits)
            {
                if (deficit.BlockPresetId != query.PresetId) continue;
                if (deficit.FocusBlockId == query.ExcludeBlockId) continue;

                var missedDate = DateOnly.FromDateTime(deficit.ScheduledStart);
                if (contract.CheckRedemptionEligibility(missedDate, proposedDate) != RedemptionEligibility.Allowed)
                    continue;

                eligible.Add((contract, deficit));
            }
        }

        // No eligible deficit found: an explicit query.ContractId is echoed back as-is
        // (this block still belongs to that contract, it just doesn't happen to cover
        // anything right now — e.g. an ordinary contract-tied block, or one whose deficit
        // was already fully covered elsewhere); auto-discovery (null) stays null, since
        // there's no reason to attach an ordinary new block to any contract at all.
        if (eligible.Count == 0)
            return new RedemptionAttributionResult(query.ContractId, []);

        // Every entry a single block ends up carrying must belong to one contract —
        // PlannedBlock.ContractId is a single value. The oldest eligible deficit across
        // every candidate decides which contract this block attaches to; only that
        // contract's deficits are considered for the spillover walk below.
        var resolvedContractId = eligible.OrderBy(e => e.Deficit.ScheduledStart).First().Contract.Id;
        var ordered = eligible
            .Where(e => e.Contract.Id == resolvedContractId)
            .OrderBy(e => e.Deficit.ScheduledStart)
            .ToList();

        var remainingDuration = (query.BlockEnd - query.BlockStart).TotalMinutes;
        var entries = new List<RedemptionEntryDto>();

        foreach (var (_, deficit) in ordered)
        {
            if (remainingDuration <= 0) break;

            var covered = Math.Min(remainingDuration, deficit.RemainingDeficitMinutes);
            if (covered <= 0) continue;

            entries.Add(new RedemptionEntryDto(deficit.FocusBlockId, covered));
            remainingDuration -= covered;
        }

        return entries.Count > 0
            ? new RedemptionAttributionResult(resolvedContractId, entries)
            : new RedemptionAttributionResult(query.ContractId, []);
    }
}
