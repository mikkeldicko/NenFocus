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
        NenContract? contract = null;

        if (query.ExistingBlockId is { } existingId)
        {
            // An existing block keeps its own contract; a Focus block's edit also recomputes
            // attribution (scoped to that contract) so the checked window is the final,
            // capped one — the same one the handler will persist.
            var existing = await _blocks.GetByIdAsync(existingId, ct);
            if (existing?.ContractId is { } contractId)
            {
                contract = await _contracts.GetByIdAsync(contractId, ct);
                if (query.Type == BlockPresetType.Focus && existing is FocusBlock focus)
                {
                    var existingEntries = focus.RedemptionEntries
                        .Select(RedemptionEntryDto.From)
                        .ToList();
                    var attribution = await _attribution.HandleAsync(
                        new ComputeRedemptionAttributionQuery(contractId, query.PresetId, query.Slot, existingId, existingEntries), ct);
                    slot = attribution.Slot;
                }
            }
        }
        else if (query.Type == BlockPresetType.Focus)
        {
            // A new Focus block may pick up a contract through a deficit — attribution
            // searches (scoped to ContractId when set) and returns the final, capped window.
            var attribution = await _attribution.HandleAsync(
                new ComputeRedemptionAttributionQuery(query.ContractId, query.PresetId, query.Slot, null), ct);
            slot = attribution.Slot;
            if (attribution.ContractId is { } contractId)
                contract = await _contracts.GetByIdAsync(contractId, ct);
        }

        // The calendar as it will be — without the block being moved/split.
        var calendar = (await _blocks.GetForRangeAsync(slot, ct)).Where(b => b.Id != query.ExistingBlockId);
        var sessions = await _sessions.GetActiveSessionsAsync(ct);

        var verdict = _checker.Check(slot, query.Type, contract, calendar, sessions, _clock.Now, _clock.TimeZone);

        return BlockPlacementVerdictDto.From(verdict);
    }
}
