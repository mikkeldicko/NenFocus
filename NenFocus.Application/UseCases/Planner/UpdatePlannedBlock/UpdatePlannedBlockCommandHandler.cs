using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;

    public UpdatePlannedBlockCommandHandler(IPlannedBlockRepository blocks, IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _profiles = profiles;
    }

    public async Task<UpdatePlannedBlockResult> HandleAsync(UpdatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdatePlannedBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return UpdatePlannedBlockResult.Fail($"Profile '{command.ProfileId}' not found.");

        if (existing.Type == BlockType.Focus)
        {
            var overlapping = await _blocks.GetOverlappingAsync(
                startTime,
                endTime,
                BlockType.Focus,
                excludeId: command.BlockId,
                cancellationToken: cancellationToken);

            if (overlapping.Count > 0)
                return UpdatePlannedBlockResult.Fail("A Focus block already exists in this time window.");
        }

        await _blocks.DeleteAsync(command.BlockId, cancellationToken);

        PlannedBlock updated = existing.Type == BlockType.Focus
            ? FocusBlock.Create(Guid.Empty, command.ProfileId, existing.Type, startTime, endTime, true)
            : PassiveBlock.Create(command.ProfileId, startTime, endTime);

        await _blocks.AddAsync(updated, cancellationToken);

        return UpdatePlannedBlockResult.Ok();
    }
}
