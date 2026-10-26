using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetBlocksForDay;

public sealed class GetBlocksForDayQueryHandler
    : IQueryHandler<GetBlocksForDayQuery, IReadOnlyList<BlockDto>>
{
    private readonly IBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;
    private readonly BlockChangeChecker _changes;

    public GetBlocksForDayQueryHandler(
        IBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        ISystemClock clock,
        BlockChangeChecker changes)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _clock = clock;
        _changes = changes;
    }

    public async Task<IReadOnlyList<BlockDto>> HandleAsync(
        GetBlocksForDayQuery query,
        CancellationToken cancellationToken = default)
    {
        // The user's calendar day as real time (Gap 97); cancelled blocks are already left out.
        var blocks = await _blocks.GetForRangeAsync(_clock.TimeZone.SlotCovering(query.Date), cancellationToken);

        var allPresets = await _presets.GetAllAsync(cancellationToken);
        var presetMap = allPresets.ToDictionary(p => p.Id);
        var now = _clock.Now;

        var contractIds = blocks.Select(b => b.Membership?.ContractId).Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        var contractsById = new Dictionary<Guid, NenContract?>();
        foreach (var contractId in contractIds)
            contractsById[contractId] = await _contracts.GetByIdAsync(contractId, cancellationToken);

        return blocks.Select(b =>
        {
            var preset = presetMap.TryGetValue(b.BlockPresetId, out var p) ? p : null;
            var focusBlock = b as FocusBlock;

            // The span the block was worked over, else its schedule (Gap 95): late and early-end
            // rules live on the block alone, so the tile and every hour count can't drift.
            var shown = focusBlock?.WorkSlot() ?? b.Scheduled;

            // The tag is the block's own stored role in its contract (Gap 97). Focus only: the
            // floor and Bonus hours only ever come from Focus blocks, so a Background block shows none.
            var bonusContract = focusBlock is { IsBonus: true }
                ? contractsById.GetValueOrDefault(focusBlock.Membership!.ContractId)
                : null;

            return new BlockDto
            {
                Id = b.Id,
                PresetId = b.BlockPresetId,
                PresetName = preset?.Name ?? "Unknown",
                BlockPresetType = b.PresetType,
                Shown = shown,
                WebsiteFilterType = preset?.WebsiteFilterType ?? WebsiteFilterType.Blocklist,
                ContractId = b.Membership?.ContractId,
                IsFinal = b.IsFinal(now),
                Allowances = _changes.AllowancesFor(
                    b, b.Membership is { } membership ? contractsById.GetValueOrDefault(membership.ContractId) : null, now),
                Outcome = focusBlock?.Outcome(now),
                StartedLate = focusBlock?.StartedLate ?? false,
                EndedEarly = focusBlock?.EndedEarly ?? false,
                ContractRole = focusBlock?.Membership?.Role,
                BonusContractName = bonusContract?.Name
            };
        }).ToList();
    }
}
