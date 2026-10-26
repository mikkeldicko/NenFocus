using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetBlockPlacementVerdict;

public sealed class GetBlockPlacementVerdictQueryHandler
    : IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto>
{
    private readonly IBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly INenContractRepository _contracts;
    private readonly BlockPlacementChecker _checker;
    private readonly ISystemClock _clock;

    public GetBlockPlacementVerdictQueryHandler(
        IBlockRepository blocks,
        ISessionRepository sessions,
        INenContractRepository contracts,
        BlockPlacementChecker checker,
        ISystemClock clock)
    {
        _blocks = blocks;
        _sessions = sessions;
        _contracts = contracts;
        _checker = checker;
        _clock = clock;
    }

    public async Task<BlockPlacementVerdictDto> HandleAsync(
        GetBlockPlacementVerdictQuery query,
        CancellationToken ct = default)
    {
        var slot = query.Slot;
        var contractsInEffect = await _contracts.GetAllInEffectAsync(ct);

        // A new block has no membership yet: the checker finds the contract its slot sits in, as
        // the Create handler does. A moved block keeps its own, and a redemption block its receipt
        // (Gap 106) — so the slot checked is the requested one, the one Update saves.
        ContractMembership? membership = null;
        if (query.ExistingBlockId is { } existingId)
            membership = (await _blocks.GetByIdAsync(existingId, ct))?.Membership;

        // The calendar as it will be — without the block being moved/split.
        var calendar = (await _blocks.GetForRangeAsync(slot, ct)).Where(b => b.Id != query.ExistingBlockId);
        var sessions = await _sessions.GetActiveAsync(ct);

        // The checker decides which contract governs: a Committed or Redemption block's own, or
        // the one whose period the slot sits in.
        var verdict = _checker.Check(slot, query.Type, membership, contractsInEffect, calendar, sessions, _clock.Now, _clock.TimeZone);

        return BlockPlacementVerdictDto.From(verdict);
    }
}
