using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INenContractRepository _contracts;
    private readonly BlockPlacementChecker _placement;
    private readonly ISystemClock _clock;

    public CreatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        BlockPlacementChecker placement,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _placement = placement;
        _clock = clock;
    }

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null) return CreatePlannedBlockResult.Fail("Preset not found.");

        // The contract the slot sits in hands out the membership — always Bonus; a redemption
        // block is made only by Cover Deficit (Gap 106). Inside no contract: a plain block.
        // Same step as GetBlockPlacementVerdictQueryHandler, which the validator ran.
        var governing = _placement.GoverningContract(
            command.Slot, await _contracts.GetAllInEffectAsync(cancellationToken), _clock.TimeZone);
        var membership = governing?.MembershipForPlacedBlock();

        PlannedBlock block = command.BlockPresetType == BlockPresetType.Focus
            ? FocusBlock.Create(Guid.NewGuid(), command.PresetId, command.Slot, membership: membership)
            : BackgroundBlock.Create(Guid.NewGuid(), command.PresetId, command.Slot, membership: membership);

        _blocks.Add(block);
        return CreatePlannedBlockResult.Ok(block.Id);
    }
}
