using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetBlockPlacementVerdict;

public sealed class GetBlockPlacementVerdictQueryHandler
    : IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _attribution;
    private readonly IBlockPlacementChecker _checker;
    private readonly ISystemClock _clock;

    public GetBlockPlacementVerdictQueryHandler(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        INenContractRepository contracts,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> attribution,
        IBlockPlacementChecker checker,
        ISystemClock clock)
    {
        _blocks = blocks;
        _sessions = sessions;
        _contracts = contracts;
        _attribution = attribution;
        _checker = checker;
        _clock = clock;
    }

    public async Task<BlockPlacementVerdictDto> HandleAsync(
        GetBlockPlacementVerdictQuery query,
        CancellationToken ct = default)
    {
        var slot = query.Slot;
        ContractMembership? membership = null;
        var contractsInEffect = await _contracts.GetAllInEffectAsync(ct);

        // The same three steps the Create/Update handlers take, so the slot and membership
        // checked here are the ones saved (Gap 97; one Domain answer after Gap 106): who governs
        // → attribution may shorten the slot → placement on the final slot.
        if (query.ExistingBlockId is { } existingId)
        {
            // Only a redemption block's coverage is recomputed on an edit, against its own contract.
            var existing = await _blocks.GetByIdAsync(existingId, ct);
            membership = existing?.Membership;
            if (existing is FocusBlock { IsRedemption: true } redemption)
            {
                var existingEntries = redemption.RedemptionEntries
                    .Select(RedemptionEntryDto.From)
                    .ToList();
                var attribution = await _attribution.HandleAsync(
                    new ComputeRedemptionAttributionQuery(membership!.ContractId, query.PresetId, query.Slot, existingId, existingEntries), ct);
                slot = attribution.Slot;
            }
        }
        else if (_checker.GoverningContract(query.Slot, contractsInEffect, _clock.TimeZone) is { } governing)
        {
            // A new block inside a contract: Redemption if it covers a deficit there, else Bonus.
            var claimsDeficit = false;
            if (query.Type == BlockPresetType.Focus && !query.DeclineRedemption)
            {
                var attribution = await _attribution.HandleAsync(
                    new ComputeRedemptionAttributionQuery(governing.Id, query.PresetId, query.Slot, null), ct);
                slot = attribution.Slot;
                claimsDeficit = attribution.Entries.Count > 0;
            }
            membership = governing.MembershipForPlacedBlock(claimsDeficit);
        }

        // The calendar as it will be — without the block being moved/split.
        var calendar = (await _blocks.GetForRangeAsync(slot, ct)).Where(b => b.Id != query.ExistingBlockId);
        var sessions = await _sessions.GetActiveSessionsAsync(ct);

        // The checker decides which contract governs: a Committed or Redemption block's own, or
        // the one whose period the slot sits in.
        var verdict = _checker.Check(slot, query.Type, membership, contractsInEffect, calendar, sessions, _clock.Now, _clock.TimeZone);

        return BlockPlacementVerdictDto.From(verdict);
    }
}
