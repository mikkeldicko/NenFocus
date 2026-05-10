using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;

    public CreatePlannedBlockCommandHandler(IPlannedBlockRepository blocks, IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _profiles = profiles;
    }

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null) return CreatePlannedBlockResult.Fail("Profile not found.");

        if (command.BlockType == BlockType.Focus)
        {
            var overlapping = await _blocks.GetOverlappingAsync(startTime, endTime, BlockType.Focus, cancellationToken: cancellationToken);
            if (overlapping.Count > 0) return CreatePlannedBlockResult.Fail("A Focus block already exists here.");
        }

        PlannedBlock block = command.BlockType == BlockType.Focus
            ? FocusBlock.Create(Guid.Empty, command.ProfileId, command.BlockType, startTime, endTime, true)
            : PassiveBlock.Create(command.ProfileId, startTime, endTime);

        await _blocks.AddAsync(block, cancellationToken);
        return CreatePlannedBlockResult.Ok(block.Id);
    }
}
