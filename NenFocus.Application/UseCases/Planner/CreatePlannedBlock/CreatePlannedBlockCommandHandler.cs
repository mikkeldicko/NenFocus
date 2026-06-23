using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;
    private readonly IValidator<CreatePlannedBlockCommand> _validator;

    public CreatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles,
        IValidator<CreatePlannedBlockCommand> validator)
    {
        _blocks = blocks;
        _profiles = profiles;
        _validator = validator;
    }

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return CreatePlannedBlockResult.Fail(validationResult.Errors.First().ErrorMessage);

        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null) return CreatePlannedBlockResult.Fail("Profile not found.");

        if (command.BlockType == BlockType.Focus)
        {
            var overlapping = await _blocks.GetOverlappingAsync(startTime, endTime, ritualOnly: true, cancellationToken: cancellationToken);
            if (overlapping.Count > 0) return CreatePlannedBlockResult.Fail("A Focus block already exists here.");
        }

        PlannedBlock block = command.BlockType == BlockType.Focus
            ? FocusBlock.Create(Guid.NewGuid(), command.ProfileId, startTime, endTime)
            : BackgroundBlock.Create(Guid.NewGuid(), command.ProfileId, startTime, endTime);

        await _blocks.AddAsync(block, cancellationToken);
        return CreatePlannedBlockResult.Ok(block.Id);
    }
}
