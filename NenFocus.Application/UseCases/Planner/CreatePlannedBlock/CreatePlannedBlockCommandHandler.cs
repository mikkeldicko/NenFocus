using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;
    private readonly IBlockPlacementChecker _placement;
    private readonly ISystemClock _clock;

    public CreatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution,
        IBlockPlacementChecker placement,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _computeRedemptionAttribution = computeRedemptionAttribution;
        _placement = placement;
        _clock = clock;
    }

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var slot = command.Slot;

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null) return CreatePlannedBlockResult.Fail("Preset not found.");

        // The contract the slot sits in decides everything about membership (Gap 97): only it is
        // asked about deficits, and it hands out the membership — Redemption when the block
        // covers one, otherwise Bonus. Inside no contract: a plain block that redeems nothing.
        // Same steps as GetBlockPlacementVerdictQueryHandler, which the validator ran.
        var governing = _placement.GoverningContract(
            slot, await _contracts.GetAllInEffectAsync(cancellationToken), _clock.TimeZone);

        IReadOnlyList<RedemptionEntry> redemptionEntries = [];
        var redemptionCapped = false;

        if (governing is not null && command.BlockPresetType == BlockPresetType.Focus && !command.DeclineRedemption)
        {
            // Attribution returns the final (capped) window — the one place the cap is computed,
            // so the validator's placement check and this save can never disagree (Gap 88 phase 8).
            var attribution = await _computeRedemptionAttribution.HandleAsync(
                new ComputeRedemptionAttributionQuery(governing.Id, command.PresetId, slot, ExcludeBlockId: null),
                cancellationToken);
            redemptionEntries = attribution.Entries
                .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                .ToList();
            slot = attribution.Slot;
            redemptionCapped = attribution.WasCapped;
        }

        var membership = governing?.MembershipForPlacedBlock(claimsDeficit: redemptionEntries.Count > 0);

        PlannedBlock block = command.BlockPresetType == BlockPresetType.Focus
            ? FocusBlock.Create(Guid.NewGuid(), command.PresetId, slot, membership: membership, redemptionEntries: redemptionEntries)
            : BackgroundBlock.Create(Guid.NewGuid(), command.PresetId, slot, membership: membership);

        await _blocks.AddAsync(block, cancellationToken);
        return CreatePlannedBlockResult.Ok(block.Id, redemptionCapped);
    }
}
